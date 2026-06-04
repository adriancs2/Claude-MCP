# MCP2

A single-binary MCP server for Claude Desktop on Windows. 63 tools across file editing, code search, SQLite, MySQL, SSH/SFTP, MSBuild, HTTP, and shell. Written in C# on .NET Framework 4.8 — one `MCP2.exe`, no runtime to install, no Node, no Python, no Docker.

![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-purple)
![C#](https://img.shields.io/badge/C%23-7.3-blue)
![License](https://img.shields.io/badge/license-Unlicense-green)
![Tools](https://img.shields.io/badge/tools-63-orange)

```
adriancs2 / Claude-MCP        Unlicense · C# · .NET 4.8
```

---

## The shape of it

```
Claude Desktop  ──stdin/stdout JSON-RPC──►  MCP2.exe
                                              │
                                              ├─ Tools/        (63 ITool classes, auto-discovered)
                                              ├─ Services/     (file ops, backup, diff, SSH, MySQL, SQLite)
                                              └─ Core/         (protocol, config, caller check)
```

Every tool is one `.cs` file implementing one interface:

```csharp
public interface ITool
{
    string Name { get; }
    string Description { get; }
    ToolParamList Params { get; }
    ToolResult Execute(JObject args);
}
```

At startup, `ToolDiscovery` reflects over the assembly, instantiates everything that implements `ITool`, and serves it. Drop a new `.cs` file into `Tools/`, rebuild, restart Claude Desktop — the tool shows up. No registration table, no manifest, no JSON schema written by hand. The schema is generated from the fluent `ToolParamList` definition on the tool itself.

---

## Two editing paradigms, both first-class

File edits are the part Claude touches most, so this is where the design pays off.

**Content-matched edits** locate the change point by *text*, not line number — so they don't break when earlier edits shift lines. `replace_string` is the safest tool in the set: by default it refuses to edit if `old_string` appears 0 or 2+ times, returning an `AMBIGUOUS_MATCH` error that explains exactly how to recover (add context, switch to `replace_string_nth`, or set `must_be_unique=false`).

```
  replace_string              must-be-unique, errors on 0 or 2+ matches
  replace_string_all          every occurrence
  replace_string_nth          the Nth occurrence, 1-based
  replace_string_regex        .NET regex with $1, $2, ${name} substitutions
```

**Line-targeted edits** are better for range work — "delete lines 50–80", "replace this 30-line block with this 12-line block".

```
  replace_line                single line
  replace_lines               range
  insert_after_line           use line=0 for top-of-file
  delete_lines                range
  batch_edit_lines            many edits, one file, one call
```

`batch_edit_lines` is the interesting one. You pass several edits referencing the file's *current* line numbers. Internally it sorts them bottom-up before applying, so line numbers stay valid through the batch — no manual offset arithmetic. One backup per file regardless of edit count, and you get back a single consolidated unified diff.

---

## Every edit returns a unified diff

This isn't decoration. The diff is computed by an LCS-based differ (`Services/UnifiedDiff.cs`, with `CompareFiles` using a full DP table for two-file compares). It aligns unchanged lines correctly — a one-line insertion at the top of a 600-line file produces one diff hunk, not 599 shifted lines. Pre-processing trims common prefix and suffix before LCS, lines are hashed to ints for fast equality, and there's a 25M-cell memory guard so a pathological 50K×50K compare doesn't OOM the host.

So after every edit, Claude sees something like:

```
@@ -7,6 +7,7 @@
     {
         public string Name { get; set; }
         public int Count { get; set; }
+        public DateTime CreatedAt { get; set; }
 
         public string Render()
         {
```

Claude can verify the change actually matches intent before moving on — and so can you, in the conversation transcript.

---

## Backups happen by default

Every file-modifying tool calls `BackupService.CreateBackup` before writing. Backups land in `./backups` next to the exe (configurable) with timestamps like `markers.txt.20260514_095422.bak`. Rapid successive edits get millisecond resolution so nothing overwrites. `undo_last_edit` restores from the most recent backup. `list_backups` shows what's available. `clear_backups` reclaims disk space by age.

`create_backup: false` is supported per-call when you really want to skip it.

---

## SSH that remembers state

`ssh_open` opens a persistent connection using a named profile from `mcp-config.json`. `ssh_send` issues a command on that session — `cd`, environment variables, shell state, all of it carries across calls. `ssh_close` ends the session.

Credentials never appear in tool parameters. You define them once in config:

```json
"ssh_profiles": {
  "myvps": {
    "host": "vps.example.com",
    "port": 22,
    "username": "root",
    "private_key_path": "C:\\Users\\you\\.ssh\\id_rsa",
    "passphrase": ""
  }
}
```

Then in conversation: `ssh_open("myvps")` and you're in. The profile name doubles as the session id, so Claude doesn't have to track tokens.

For one-shot transfers, `ssh_upload` and `ssh_download` skip the open/close dance entirely — pass a mix of files and directories (directories transfer recursively), and the SFTP connection opens, transfers, and closes in a single call.

---

## MSBuild that auto-discovers Visual Studio

```
msbuild(project: "src/MyApp.sln", target: "Rebuild", configuration: "Release")
```

No config. The tool scans `C:\Program Files\Microsoft Visual Studio\{version}\{edition}\MSBuild\Current\Bin\MSBuild.exe`, picks the highest version number, checks Community → Professional → Enterprise in that order, and caches the path for the process. Upgrade Visual Studio and it just keeps working.

Output is post-processed: errors are always shown, warnings are summarized to a count by default (`show_warnings: true` to expand). The warning-line regex handles 4-tuple spans (VS 2022+), `N>` multi-proc prefixes, and `MSBUILD : warning MSBxxxx` lines.

Supports `Build`, `Rebuild`, `Clean`, `Restore` on `.csproj`, `.sln`, and `.slnx`.

---

## MySQL with batching and variable passing

Eight MySQL tools, one connection string in `mcp-config.json`. The interesting ones:

`batch_mysql_queries_with_variables` lets a sequence of queries share state — `@last_id := LAST_INSERT_ID()` in step 1 is available in step 2. Useful for multi-step inserts where the FK depends on the new PK without an extra round-trip.

`mysql_schema` reports databases / tables / columns / `CREATE TABLE` in one tool, switched by `info_type`. `batch_mysql_schema` pulls structure for multiple tables in one call — handy when Claude is reading several related tables before writing a query.

`mysql_select` returns result sets, `mysql_execute` returns affected rows, `mysql_scalar` returns a single value (for `COUNT(*)`, `MAX(...)`, etc.) — separate tools mean Claude picks the right shape and the response stays small.

---

## SQLite, zero-config and full-trust

Four SQLite tools, built specifically for Claude Code. Unlike MySQL there's no connection string to configure — every call takes a file path directly, with direct physical access to production database files.

`sqlite_query` is one combined read+write tool. Pass a single `sql` string or an array of `statements` that run atomically in a single transaction — any failure rolls the whole batch back and names the offending statement. `SELECT`/`PRAGMA` return formatted rows (default 50, override with `max_rows`); `INSERT`/`UPDATE`/`DELETE`/DDL report affected rows. Output comes in four formats — `ascii`, `json` (typed values, not stringified), `csv`, `markdown`.

`sqlite_schema` reports columns, indexes, and `CREATE` statements for one table, several, or `["*"]` for all. `sqlite_list_databases` recursively discovers `.db`/`.sqlite`/`.sqlite3`/`.db3` files under a directory. `sqlite_create_database` explicitly creates a new file — kept separate from `sqlite_query` so a typo'd path can't silently spawn a stray database.

Backed by `Stub.System.Data.SQLite.Core` — the native engine ships in the binary, no external SQLite install needed.

---

## Process-identity check

`CallerValidator` runs once at startup, walks up to two levels of parent processes via WMI, and checks the executable path against the Claude Desktop install patterns:

```
C:\Users\{user}\AppData\Local\AnthropicClaude\app-{n}.{n}.{n}\claude.exe
C:\Program Files\WindowsApps\Claude_{version}_{arch}__{hash}\app\Claude.exe
```

Both legacy and MSIX install paths are recognized. If the parent isn't Claude Desktop (and the grandparent isn't either, since Claude can spawn via `cmd.exe`), startup throws and the process exits. There's a `MCP_BYPASS_VALIDATION` env var if you need to integrate from elsewhere, but the default behavior keeps the binary from being a generic exec-anything endpoint that another app on the machine could shell into.

---

## Auto-discovery and zero registration

`ToolDiscovery.DiscoverTools()` is 30 lines. It reflects the assembly, finds every concrete `ITool`, calls the parameterless constructor, registers by `Name`. If one tool throws during construction it logs to stderr and keeps going — one broken tool doesn't kill the server.

JSON schemas for the MCP protocol are generated from the same `ToolParamList` you use to declare params in C#:

```csharp
public ToolParamList Params => new ToolParamList()
    .String("path", "Full path to the file", required: true)
    .String("old_string", "Text to find...", required: true)
    .String("new_string", "Replacement text...", required: true)
    .Bool("must_be_unique", "...", defaultValue: true)
    .Bool("case_sensitive", "Case-sensitive match", defaultValue: true);
```

`ToolDiscovery.GenerateToolDefinitions` walks the same list, emits the JSON schema with proper types, defaults, enums, and required fields. Description, schema, and runtime validation all share one source.

---

## The full tool list

**File operations (11):** `read_file` · `count_lines` · `find_pattern` · `find_all_occurrences` · `write_file` · `append_to_file` · `copy_file` · `move_file` · `delete_file` · `file_exists` · `get_file_info`

**File editing (9):** `replace_line` · `replace_lines` · `insert_after_line` · `delete_lines` · `batch_edit_lines` · `replace_string` · `replace_string_all` · `replace_string_nth` · `replace_string_regex`

**Directory (6):** `list_directory` · `create_directory` · `copy_directory` · `move_directory` · `delete_directory` · `batch_copy_files`

**Search (2):** `find_in_files` · `replace_in_files` (preset exclusions: `dotnet`, `web`, `python`)

**Backup & diff (6):** `backup_file` · `undo_last_edit` · `list_backups` · `compare_files` · `diff_preview` · `clear_backups`

**Batch read (1):** `batch_read_files` — full files or per-entry line ranges in one call

**MySQL (8):** `mysql_execute` · `mysql_select` · `mysql_scalar` · `mysql_schema` · `mysql_test` · `batch_mysql_schema` · `batch_mysql_queries` · `batch_mysql_queries_with_variables`

**SQLite (4):** `sqlite_query` · `sqlite_schema` · `sqlite_list_databases` · `sqlite_create_database`

**HTTP (3):** `http_get` · `http_post` · `http_request`

**Zip (5):** `zip_file` · `zip_folder` · `extract_zip` · `extract_zip_content` · `list_zip_contents`

**Image (1):** `view_image` — base64 round-trip so Claude can see images on disk

**Shell (1):** `run_command` — PowerShell, cmd, or any executable; stdout/stderr returned

**Build (1):** `msbuild`

**SSH (5):** `ssh_open` · `ssh_send` · `ssh_close` · `ssh_upload` · `ssh_download`

---

## Setup

### 1. Get a build

Grab `MCP2.zip` from [Releases](https://github.com/adriancs2/Claude-MCP/releases), or open `src/MCP2.slnx` in Visual Studio and build it yourself.

### 2. Drop it somewhere

```
D:\Claude Files\MCP2\
  ├─ MCP2.exe
  ├─ mcp-config.json     (auto-generated on first run if missing)
  └─ backups\            (created on first edit)
```

### 3. Configure (all fields optional)

```json
{
  "mysql_connection_string": "Server=localhost;Database=mydb;User=root;Password=secret;",
  "gc_memory_threshold_mb": 150,
  "debug_logging": false,
  "backup_directory": null,
  "ssh_profiles": {
    "myserver": {
      "host": "192.168.1.100",
      "port": 22,
      "username": "admin",
      "password": "your-password"
    }
  }
}
```

Skip the MySQL string if you don't use MySQL. Skip `ssh_profiles` if you don't use SSH. The MySQL tools error politely when called without a connection string — they don't crash the server.

### 4. Point Claude Desktop at it

Edit `claude_desktop_config.json` (`%APPDATA%\Claude\` or the MSIX equivalent):

```json
{
  "mcpServers": {
    "mcp2": {
      "command": "D:\\Claude Files\\MCP2\\MCP2.exe",
      "args": []
    }
  }
}
```

Restart Claude Desktop. Ask it to `list_directory` on any folder to confirm.

---

## Adding a tool

Create `Tools/Misc/EchoTool.cs`:

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

Rebuild. Restart Claude Desktop. `echo` is now in the tool list. No registration, no manifest, no schema authoring — the `ToolParamList` fluent builder is the schema.

---

## Dependencies

| Package | Purpose |
|---------|---------|
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/) | JSON-RPC parsing |
| [MySqlConnector](https://www.nuget.org/packages/MySqlConnector/) | MySQL access |
| [Stub.System.Data.SQLite.Core](https://www.nuget.org/packages/Stub.System.Data.SQLite.Core.NetFramework/) | SQLite access (native engine bundled) |
| [SSH.NET](https://www.nuget.org/packages/SSH.NET/) | SSH + SFTP |

That's it. Everything else — diff algorithm, backup management, MSBuild discovery, caller validation, JSON schema generation — is project code in `src/MCP2/`.

---

## Further reading

- [Documentation: Writing MCP Tools in C# (.NET Framework) for Claude Desktop/Code](https://adriancs.com/documentation-writing-mcp-tools-in-c-net-framework-for-claude-desktop-code/)
- [Building a Self-Improving MCP Server Tool for Claude Desktop in C# (Console App)](https://adriancs.com/building-a-self-improving-mcp-server-tool-for-claude-desktop-in-c-console-app/)

---

## License

[The Unlicense](https://unlicense.org/) — public domain. Take it, fork it, ship it, sell it. No attribution required.
