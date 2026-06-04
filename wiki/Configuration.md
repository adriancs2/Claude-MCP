# Configuration

Two files to know about:

1. **`mcp-config.json`** — sits next to `MCP2.exe`, controls MCP2's own behavior (MySQL connection, SSH profiles, backup location, logging).
2. **The client config** — tells Claude Desktop or Claude Code that MCP2 exists. Lives in a different folder depending on which client you use.

---

## 1. `mcp-config.json` — MCP2's own settings

This file is auto-generated next to `MCP2.exe` on first run if it doesn't exist. You can edit it any time — MCP2 reads it at startup.

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
    },
    "myvps": {
      "host": "vps.example.com",
      "port": 22,
      "username": "root",
      "private_key_path": "C:\\Users\\you\\.ssh\\id_rsa",
      "passphrase": ""
    }
  }
}
```

**All fields are optional.** If you don't use MySQL, leave the connection string empty — the MySQL tools will error politely when called without crashing the server. If you don't use SSH, omit `ssh_profiles` entirely.

> **SQLite needs no configuration.** The four `sqlite_*` tools take a database file path as a call parameter, so there's nothing to set up here — they work out of the box, and the native SQLite engine is bundled in the binary.

| Setting | Description | Default |
|---|---|---|
| `mysql_connection_string` | MySQL connection string | (empty) |
| `gc_memory_threshold_mb` | Memory threshold to trigger garbage collection | 150 |
| `debug_logging` | Write debug log to `mcp_debug.log` next to the exe | false |
| `backup_directory` | Custom path for backup files | `./backups` next to exe |
| `ssh_profiles` | Named SSH connection profiles | (none) |

### SSH profile shape

Each profile under `ssh_profiles` supports either password auth or private-key auth:

```json
"profile_name": {
  "host": "...",            // required
  "port": 22,               // default 22
  "username": "...",        // required
  "password": "...",        // password auth
  "private_key_path": "...", // private-key auth (mutually exclusive with password)
  "passphrase": "..."       // optional key passphrase
}
```

The profile name is what you pass to `ssh_open` — it also doubles as the session identifier for `ssh_send` and `ssh_close`.

---

## 2. Client config — telling the client about MCP2

This is the part that differs between Claude Desktop and Claude Code. Pick the section that matches your client.

### A. Claude Desktop (Windows)

There are two ways to reach the config file.

**Route 1 — from inside Claude Desktop:**

> Settings → Developer → **Edit Config**

This opens the folder containing the config file in Windows Explorer.

**Route 2 — directly via Windows Explorer:**

```
C:\Users\{user_profile}\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\
```

Either route lands you at the same place. The file you want is:

```
claude_desktop_config.json
```

Add an `mcpServers` block — or insert MCP2 into the existing one — alongside whatever other config is already in there:

```json
{
  "mcpServers": {
    "mcp2": {
      "command": "D:\\Claude Files\\MCP2\\github\\src\\MCP2\\bin\\Release\\MCP2.exe",
      "args": []
    }
  },
  "preferences": {
    "legacyQuickEntryEnabled": false,
    "launchPreviewPersistSession": false,
    "launchPreviewPersistedWorkspaces": []
  }
}
```

**Notes:**

- Use **double backslashes** (`\\`) in the path — single backslashes are escape characters in JSON.
- Replace the `command` path with wherever your `MCP2.exe` actually lives. If you downloaded the release zip, point it at the extracted `.exe`. If you built from source, point it at `bin\Release\MCP2.exe` or `bin\Debug\MCP2.exe`.
- Leave whatever else is already in the file (`preferences`, etc.) untouched — just add or merge the `mcpServers` key.

**Restart Claude Desktop** after saving. Ask Claude to `list_directory` on any folder to confirm MCP2 is loaded.

---

### B. Claude Code

If Claude Code is installed alongside Claude Desktop, the default executable is at:

```
C:\Users\{user_profile}\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\x.x.xxx\claude.exe
```

(Replace `x.x.xxx` with your installed version number.)

The config file Claude Code reads is:

```
C:\Users\{user_profile}\.claude.json
```

Insert (or edit) `mcpServers` as one of the root parameters:

```json
{
  "some_other_section": {
    "...": "..."
  },
  "mcpServers": {
    "mcp2": {
      "type": "stdio",
      "command": "D:\\Claude Files\\MCP2\\github\\src\\MCP2\\bin\\Release\\MCP2.exe",
      "args": [],
      "cwd": "D:\\Claude Files\\MCP2_for_Code\\github\\src\\MCP2\\bin\\Release"
    }
  },
  "some_other_section_2": {
    "...": "..."
  }
}
```

**Notes specific to Claude Code:**

- `type: "stdio"` is explicit here. Claude Code uses it to know how to launch the server.
- `cwd` sets the working directory for the spawned process. Useful when MCP2's `mcp-config.json` sits next to the exe — Claude Code will launch the process with that as the working directory, so MCP2 finds its own config without you needing to set absolute paths everywhere.
- You can run a separate MCP2 build for Claude Code if you want — point `command` and `cwd` at a different folder. That lets Desktop and Code each have their own `mcp-config.json` (and their own backups).

---

### C. (Recommended for Claude Code) Disable built-in file tools

Claude Code ships with its own `Read`, `Edit`, `Write`, `Glob`, `Grep`, and `Bash` tools. If you want MCP2 to be the canonical way Claude touches files — for consistent backups, unified diffs, and the `must_be_unique` safety on edits — you can deny the built-ins.

Edit:

```
C:\Users\{user_profile}\.claude\settings.json
```

```json
{
  "env": {
    "CLAUDE_CODE_EXPERIMENTAL_AGENT_TEAMS": "1"
  },
  "extraKnownMarketplaces": {
    "claude-plugins-official": {
      "source": {
        "source": "github",
        "repo": "anthropics/claude-plugins-official"
      }
    }
  },
  "theme": "dark",
  "permissions": {
    "deny": [
      "Read",
      "Glob",
      "Grep",
      "Edit",
      "Write",
      "Bash"
    ]
  },
  "mcpServers": {
    "mcp2": {
      "command": "D:\\Claude Files\\MCP2\\src\\MCP2\\MCP2\\bin\\Debug\\net48\\MCP2.exe",
      "args": []
    },
    "web-browser": {
      "command": "D:\\Claude Files\\MCP-Web-Browser\\MCP-Web-Browser\\bin\\Debug\\MCP-Web-Browser.exe",
      "args": []
    }
  }
}
```

With these denied, Claude falls through to MCP2's `read_file`, `find_pattern`, `find_in_files`, `replace_string`, `write_file`, and `run_command` — and every file edit goes through the backup-and-diff pipeline.

This is a preference, not a requirement. Some workflows benefit from having both available. The denial list is easy to adjust.

---

## Verifying the connection

Once the config is saved, here's how to confirm everything's wired up.

### Claude Desktop

Restart Claude Desktop. In a new conversation, ask:

> "List the tools you have available."

Or just ask Claude to call `list_directory` on a folder. If MCP2 is connected you'll see it in the tool list and the call will succeed.

### Claude Code

Open a terminal and browse to the Claude Code folder:

```
C:\Users\{user_profile}\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude-code\x.x.xxx\
```

Authenticate (first run only):

```
claude.exe /login
```

Check MCP connection status:

```
claude.exe mcp list
```

Expected output:

```
claude.ai Google Drive: https://drivemcp.googleapis.com/mcp/v1   - ! Needs authentication
claude.ai Google Calendar: https://calendarmcp.googleapis.com/mcp/v1 - ! Needs authentication
claude.ai Gmail: https://gmailmcp.googleapis.com/mcp/v1          - ! Needs authentication
mcp2: D:\Claude Files\MCP2\github\src\MCP2\bin\Release\MCP2.exe  - ✓ Connected
```

A `✓ Connected` next to `mcp2` means you're done.

---

## Troubleshooting

**MCP2 doesn't show up after restart.**
Double-check the path in `command` — it must be an absolute path with double backslashes, and the file must exist. A typo or a missing exe is the most common cause.

**`Failed to validate caller`.**
MCP2 verifies its parent process is Claude Desktop or Claude Code. If you're launching it some other way (testing from a terminal, running under a different orchestrator), set the env var:

```
MCP_BYPASS_VALIDATION=1
```

For normal use with Claude clients, leave this alone.

**MySQL tools error out.**
Run `mysql_test` first. If it can't connect, the connection string in `mcp-config.json` is wrong or the server isn't reachable.

**SSH `profile not found`.**
The profile name in `mcp-config.json` is case-insensitive but spelling-sensitive. The tool's error message lists all available profiles to help you spot the typo.

**SQLite tools fail with a `SQLite.Interop.dll` / `Unable to load DLL` error.**
The SQLite engine ships as a native DLL that must sit next to `MCP2.exe`. A normal build places it in `x86\` and `x64\` subfolders of the output directory — keep those folders alongside the exe when you copy or deploy the build. If they're missing, rebuild, or copy the `x86`/`x64` folders from `bin\Release` (or `bin\Debug`).

**Backups filling up disk.**
By default backups go to `./backups` next to the exe. Use `clear_backups` with a day-count to prune old ones, or set `backup_directory` in `mcp-config.json` to a path on a different drive.

**Debug logging.**
Set `"debug_logging": true` in `mcp-config.json`. MCP2 will write detailed logs to `mcp_debug.log` next to the exe — useful for diagnosing tool-call parameter issues.
