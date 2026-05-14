# Architecture

How MCP2 is put together internally — for anyone who wants to understand the codebase, extend it, or borrow parts of it.

---

## Directory layout

```
MCP2/
├── Program.cs                    # Entry point, stdin/stdout JSON-RPC loop
├── McpServer.cs                  # MCP protocol handler
├── Core/
│   ├── ITool.cs                  # Tool interface (Name, Description, Params, Execute)
│   ├── ToolDiscovery.cs          # Auto-discovers all ITool implementations
│   ├── ToolResult.cs             # Standardized success/error responses
│   ├── McpConfig.cs              # Configuration loader (incl. SSH profiles)
│   ├── CallerValidator.cs        # Claude Desktop / Code process validation
│   ├── JsonRpcModels.cs          # JSON-RPC 2.0 request/response models
│   └── McpModels.cs              # MCP protocol models
├── Services/
│   ├── FileOperations.cs         # Core file read/write/edit logic
│   ├── BackupService.cs          # Timestamped backup management
│   ├── MySqlService.cs           # MySQL connection and query execution
│   ├── HttpService.cs            # HTTP request handling
│   ├── SshSessionManager.cs      # Persistent SSH connection management
│   ├── SftpHelper.cs             # SFTP connection factory and utilities
│   ├── MsBuildDiscovery.cs       # Auto-discovers MSBuild.exe from Visual Studio
│   └── UnifiedDiff.cs            # Unified-diff generation for edit responses
└── Tools/                        # One class per tool, auto-discovered
    ├── FileOperation/            # read, write, copy, move, batch read, etc.
    ├── FileEdit/                 # line-based and content-based editing
    ├── Directory/                # list, create, copy, move, delete
    ├── Search/                   # find_in_files, replace_in_files
    ├── Backup/                   # backup, undo, compare, diff
    ├── MySql/                    # queries, schema, batch operations
    ├── Http/                     # GET, POST, generic request
    ├── Zip/                      # create, extract, list archives
    ├── Image/                    # view_image (base64)
    ├── Shell/                    # run_command
    ├── Build/                    # msbuild (auto-discovers Visual Studio)
    └── Ssh/                      # ssh_open, ssh_send, ssh_close, ssh_upload, ssh_download
```

`Tools/` is partitioned by category, but the category folders are just organizational — they're not separate assemblies and they don't affect discovery. Move a tool between folders and it still works.

---

## The runtime path

```
Claude Desktop / Code
        │
        │ stdin/stdout JSON-RPC
        ▼
   Program.cs              ← startup: caller validation, config load, tool discovery
        │
        ▼
   McpServer.cs            ← protocol loop: parse request, dispatch, serialize response
        │
        ▼
   Tool.Execute(args)      ← one of the 59 ITool implementations
        │
        ▼
   Services/...            ← shared logic (file ops, backup, diff, SSH, MySQL, ...)
```

**Startup** happens once:

1. `CallerValidator.Validate()` — walk up the parent process chain via WMI, check the executable path against the Claude Desktop / Code install patterns. Reject if neither parent nor grandparent matches. Bypassable with `MCP_BYPASS_VALIDATION=1`.
2. `McpConfig.Load()` — auto-create `mcp-config.json` if missing, parse it, hydrate static properties.
3. `ToolDiscovery.DiscoverTools()` — reflect over the assembly, instantiate every concrete `ITool`, register by name.
4. `ToolDiscovery.GenerateToolDefinitions(...)` — convert each tool's fluent `ToolParamList` into a JSON schema for the MCP `tools/list` response.

**Request handling** is a tight loop:

1. Read one JSON-RPC message from stdin.
2. Look up the tool by name in the dictionary built at startup.
3. Call `Execute(JObject args)`.
4. Serialize the returned `ToolResult` (success text or error code + message) as a JSON-RPC response.
5. Write to stdout. Flush.

No async, no threading drama — stdin/stdout transport with line-delimited JSON.

---

## The `ITool` interface

This is the entire contract every tool implements:

```csharp
public interface ITool
{
    string Name { get; }                  // kebab-case, unique
    string Description { get; }            // shown to Claude in tool listing
    ToolParamList Params { get; }          // fluent param builder → JSON schema
    ToolResult Execute(JObject args);      // do the thing
}
```

Four members. The fluent `ToolParamList` builder declares parameters in C# and doubles as the JSON-schema source:

```csharp
public ToolParamList Params => new ToolParamList()
    .String("path", "Full path to the file", required: true)
    .String("old_string", "Text to find...", required: true)
    .String("new_string", "Replacement text...", required: true)
    .Bool("must_be_unique", "...", defaultValue: true)
    .Bool("case_sensitive", "Case-sensitive match", defaultValue: true)
    .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);
```

`ToolDiscovery.GenerateToolDefinitions` walks the same `ToolParamList` and emits a proper JSON schema — types, defaults, enums, required-field array. **The description, the runtime parameter parsing, and the schema Claude sees all share one source.** That means a parameter rename or default-value change happens in one place.

Supported parameter types: `String`, `StringEnum` (with allowed values), `Int`, `Bool`, `Array`, `Object`.

---

## Tool discovery

`ToolDiscovery.DiscoverTools()` is about 30 lines:

```csharp
var assembly = Assembly.GetExecutingAssembly();
var toolTypes = assembly.GetTypes()
    .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

foreach (var type in toolTypes)
{
    try
    {
        var tool = (ITool)Activator.CreateInstance(type);
        if (!string.IsNullOrEmpty(tool.Name))
            tools[tool.Name] = tool;
    }
    catch (Exception ex)
    {
        // Log to stderr but keep going — one broken tool shouldn't kill the server
        Console.Error.WriteLine($"Failed to instantiate tool {type.Name}: {ex.Message}");
    }
}
```

Each tool needs a parameterless constructor. Names must be unique. If two tools claim the same `Name`, the second one wins — there's no validation guard. Worth keeping in mind when copy-pasting a tool to bootstrap a new one.

The pattern is intentionally minimal: there's no registration list to keep in sync, no manifest file, no auto-generated wiring code. Drop a `.cs` file under `Tools/`, rebuild, restart the client. Done.

---

## Design decisions

### Two editing paradigms

Both line-based and content-based editing are first-class, because each has strengths.

**Line-based** (`replace_line`, `replace_lines`, `batch_edit_lines`, etc.) is best for range operations: "delete lines 50–80", "replace lines 12–35 with this block". `batch_edit_lines` auto-sorts edits bottom-up so line numbers stay valid across multiple edits in one call. The tradeoff: line numbers go stale after any edit that adds or removes lines, so they shouldn't be re-used across separate tool calls without a fresh read.

**Content-based** (`replace_string`, `replace_string_all`, `replace_string_nth`, `replace_string_regex`) is best for targeted single-spot edits: "find this exact block and replace it with this". Immune to stale line numbers. `replace_string` with `must_be_unique=true` (the default) is the safest of all the edit tools — it refuses to edit if the match is ambiguous, which makes it nearly impossible for Claude to accidentally edit the wrong location.

Heuristic: content-based for precision, line-based for range work, `batch_edit_lines` when you need multiple edits in one pass.

### Why every edit returns a unified diff

`Services/UnifiedDiff.cs` generates a unified diff for every successful edit (and `CompareFiles` does the same for two-file diffs, with a real LCS DP algorithm). The output looks exactly like `git diff`:

```
@@ -7,6 +7,7 @@
     {
         public string Name { get; set; }
         public int Count { get; set; }
+        public DateTime CreatedAt { get; set; }
 
         public string Render()
         {
```

This serves two purposes:

1. **Claude can self-verify.** After every edit it sees the actual change in standard form and can confirm it matches intent before moving on. This catches bugs where the right tool was called with subtly wrong arguments.
2. **You can audit.** The conversation transcript becomes a complete edit log. Every change is auditable in a format any developer already knows how to read.

The diff is LCS-based, so unchanged lines align correctly across insertions and deletions. Common prefix/suffix trimming and line-hash optimization keep it fast on real files. A 25M-cell memory guard prevents pathological inputs from exhausting RAM.

### Automatic backups

Every file-modifying tool calls `BackupService.CreateBackup` before writing. Backups land in `./backups` next to the exe (configurable) with timestamps like `<filename>.20260514_095422.bak`. Rapid successive edits append milliseconds to avoid filename collisions.

`undo_last_edit` restores from the most recent backup. `list_backups` shows what's available. `clear_backups` reclaims disk by age.

`create_backup: false` is supported on every edit tool when you genuinely want to skip backup — useful for repetitive automated edits where the backup churn isn't worth it. The default is always `true`.

### MSBuild auto-discovery

`Services/MsBuildDiscovery.cs` scans:

```
C:\Program Files\Microsoft Visual Studio\{version}\{edition}\MSBuild\Current\Bin\MSBuild.exe
```

It picks the highest version number and checks editions in order: Community → Professional → Enterprise. The discovered path is cached for the process lifetime. Upgrade Visual Studio and the tool keeps working without any config change.

The `msbuild` tool itself post-processes output: errors are always shown verbatim; warnings are summarized to a count by default (`show_warnings: true` expands them). The warning-line regex tolerates 4-tuple span format (VS 2022+), `N>` multi-proc prefixes, and `MSBUILD : warning MSBxxxx` lines — so the count is accurate across different MSBuild output styles.

### SSH profile-based authentication

SSH credentials live in `mcp-config.json` under `ssh_profiles`, not in tool parameters. This keeps passwords and key paths out of the conversation transcript entirely. Each profile supports either password auth or private-key auth with optional passphrase.

The profile name doubles as the session identifier — so `ssh_open("myvps")` opens a session named `myvps`, and `ssh_send` / `ssh_close` reference that same name. No session-token bookkeeping for Claude to track.

### Caller validation

`Core/CallerValidator.cs` runs once at startup. It walks up to two levels of parent processes via WMI (Claude may spawn via `cmd.exe`, so checking just the immediate parent isn't enough) and matches the executable path against both Claude Desktop install patterns:

```
C:\Users\{user}\AppData\Local\AnthropicClaude\app-{n}.{n}.{n}\claude.exe
C:\Program Files\WindowsApps\Claude_{version}_{arch}__{hash}\app\Claude.exe
```

If neither matches, startup throws `UnauthorizedAccessException` and the process exits. The intent is that this binary shouldn't be a generic "exec anything" endpoint another app on the machine could shell into.

`MCP_BYPASS_VALIDATION=1` (or `=true`) skips the check — useful for development, testing, or integrating with non-Claude orchestrators.

---

## Adding a new tool

Worked example. Create `Tools/Misc/EchoTool.cs`:

```csharp
using MCP2.Core;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Misc
{
    public class EchoTool : ITool
    {
        public string Name => "echo";
        public string Description => "Echo a string back. Useful for sanity-checking the connection.";

        public ToolParamList Params => new ToolParamList()
            .String("text", "Text to echo", required: true);

        public ToolResult Execute(JObject args)
        {
            string text = args.Value<string>("text") ?? "";
            return ToolResult.Success(text);
        }
    }
}
```

Rebuild the solution. Restart the client (Claude Desktop or Claude Code). The `echo` tool is now in the tool list — no registration, no manifest, no schema authoring.

Notes from experience:

- **Pick a unique `Name`.** It's the key in the tool dictionary; a collision silently overwrites.
- **`Description` is what Claude reads.** Be specific about what the tool does, when to use it, and what it returns. Include any non-obvious constraints (uniqueness requirements, line-number semantics, retry semantics). Claude treats this as its primary documentation.
- **Return early with `ToolResult.Error` on bad input.** It produces a structured error response that Claude can read and recover from. A thrown exception works but is less helpful — Claude sees an opaque internal error.
- **Use `Services/` for anything shared.** Backup, diff, file-encoding handling, MySQL connections — there's existing infrastructure. New tools that touch files should go through `FileOperations.cs` for consistent UTF-8 + BOM handling, line-ending normalization, and so on.
- **Backups are opt-out, not opt-in.** If your tool modifies a file, call `BackupService.CreateBackup(path)` first unless `create_backup` was explicitly set to false in the args.

---

## Dependencies

| Package | Purpose |
|---|---|
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/) | JSON-RPC parsing |
| [MySqlConnector](https://www.nuget.org/packages/MySqlConnector/) | MySQL access |
| [SSH.NET](https://www.nuget.org/packages/SSH.NET/) | SSH + SFTP |

That's all the runtime dependencies. Everything else — the diff algorithm, backup management, MSBuild discovery, caller validation, JSON-schema generation, the protocol loop — is project code in `src/MCP2/`. The target framework is .NET Framework 4.8 (C# 7.3), single-binary deploy via `bin/Release/MCP2.exe` plus its three referenced DLLs.

---

## Further reading

- [Documentation: Writing MCP Tools in C# (.NET Framework) for Claude Desktop/Code](https://adriancs.com/documentation-writing-mcp-tools-in-c-net-framework-for-claude-desktop-code/)
- [Building a Self-Improving MCP Server Tool for Claude Desktop in C# (Console App)](https://adriancs.com/building-a-self-improving-mcp-server-tool-for-claude-desktop-in-c-console-app/)
- `system-prompts.txt` (in the repo root) — full tool reference formatted for pasting into a Claude Desktop system prompt or Claude Project instructions.
