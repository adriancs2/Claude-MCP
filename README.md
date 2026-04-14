# MCP2 — Enhanced MCP Tool Server for Claude Desktop

A unified MCP (Model Context Protocol) server that gives Claude Desktop 90+ tools for file operations, code editing, shell commands, database access, SSH remote access, project building, HTTP requests, and more. Built as a single .NET Framework 4.8 console application.

![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-purple)
![C#](https://img.shields.io/badge/C%23-7.3-blue)
![License](https://img.shields.io/badge/license-Unlicense-green)
![Tools](https://img.shields.io/badge/tools-90+-orange)

> **Note:** This is designed for **Claude Desktop** on Windows. Claude Code already has built-in tools that cover most of this functionality — though if you use Claude Code and need MySQL, SSH, or MSBuild tools, you can extract those sections from this project.

---

## Documentation - How to Build MCP in C# Console App

- [Documentation: Writing MCP Tools in C# (.NET Framework) for Claude Desktop/Code
](https://adriancs.com/documentation-writing-mcp-tools-in-c-net-framework-for-claude-desktop-code/)
- [Building a Self-Improving MCP Server Tool for Claude Desktop in C# (Console App)](https://adriancs.com/building-a-self-improving-mcp-server-tool-for-claude-desktop-in-c-console-app/)

---

## Why This Exists

Claude Desktop's built-in file tools are limited. You can't:

- Edit a specific line by number
- Target the 3rd occurrence of a pattern when there are 10 matches
- Make multiple edits to a file without line numbers shifting
- Replace a block of code with content-matching safety (like `str_replace`, but better)
- Execute MySQL queries
- Run shell commands and get the output back
- SSH into remote servers and execute commands interactively
- Transfer files to/from remote servers via SFTP
- Build .NET Framework projects from the conversation

---

## Quick Start

### 1. Download

Download `MCP2.zip` from the [releases page](https://github.com/adriancs2/Claude-MCP/releases).

### 2. Extract

Extract to a folder of your choice:

```
D:\Claude Files\MCP2
```

### 3. Configure

Edit `mcp-config.json` in the same folder as the `.exe`:

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

All fields are optional. If you don't use MySQL, leave the connection string empty. If you don't use SSH, omit the `ssh_profiles` section.

| Setting | Description | Default |
|---------|-------------|---------|
| `mysql_connection_string` | MySQL connection string | (empty) |
| `gc_memory_threshold_mb` | Memory threshold to trigger garbage collection | 150 |
| `debug_logging` | Write debug log to `mcp_debug.log` | false |
| `backup_directory` | Custom path for backup files | `./backups` next to exe |
| `ssh_profiles` | Named SSH connection profiles (see SSH Tools) | (none) |

### 4. Configure Claude Desktop

Edit `C:\Users\{username}\AppData\Local\Packages\Claude_pzs8sxrjxfjjc\LocalCache\Roaming\Claude\claude_desktop_config.json`:

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

Replace the path with your actual installation path. Use double backslashes (`\\`).

### 5. Restart Claude Desktop

The tools will appear in Claude Desktop's tool list. Ask Claude to `list_directory` on any folder to verify.

---

## What's In the Box

### File Operations (13 tools)

Read, write, copy, move, delete files. Search for patterns with line numbers. Count lines. Check file existence and metadata.

`read_file` · `read_file_lines` · `read_line_range` · `count_lines` · `find_pattern` · `find_all_occurrences` · `write_file` · `append_to_file` · `copy_file` · `move_file` · `delete_file` · `file_exists` · `get_file_info`

### File Editing (12 tools)

Two approaches — **line-based** and **content-based** — use whichever fits the situation.

**Line-based editing** — when you know the line numbers:

`replace` · `replace_line_range` · `insert_at` · `insert_after` · `delete` · `batch_edit` · `replace_in_line_range`

**Content-based editing** — when you know the text to find:

`replace_first` · `replace_all` · `edit_nth_occurrence` · `replace_regex`

> **`replace_first` with `must_be_unique`** — This is the safest content-based edit tool. By default, it requires the find text to appear exactly once in the file, rejecting the edit if there are 0 or 2+ matches. This prevents accidental edits when the match is ambiguous — similar to Anthropic's `str_replace` pattern, but with the option to disable the uniqueness check when you explicitly want first-match behavior. Supports multi-line blocks.

### Directory Operations (7 tools)

`list_directory` · `list_allowed_directories` · `create_directory` · `copy_directory` · `move_directory` · `delete_directory` · `batch_copy_files`

### Search Tools (2 tools)

Search and replace across all files in a directory with filtering and presets.

`find_in_files` · `replace_in_files`

### Backup & Safety (6 tools)

Every file edit creates a timestamped backup automatically. One-command undo.

`backup_file` · `undo_last_edit` · `list_backups` · `compare_files` · `diff_preview` · `clear_backups`

### Batch Read (2 tools)

Read multiple files or specific line ranges from multiple files in one call.

`batch_read_files` · `batch_read_files_ranges`

### MySQL Database (8 tools)

Full database access — queries, schema exploration, batch operations with variable passing.

`mysql_execute` · `mysql_select` · `mysql_scalar` · `mysql_schema` · `mysql_test` · `batch_mysql_schema` · `batch_mysql_queries` · `batch_mysql_queries_with_variables`

### HTTP (3 tools)

`http_get` · `http_post` · `http_request`

### Zip (5 tools)

`zip_file` · `zip_folder` · `extract_zip` · `extract_zip_content` · `list_zip_contents`

### Image (1 tool)

`view_image` — Returns image as base64 for Claude to analyze visually.

### Shell (1 tool)

`run_command` — Execute any program (PowerShell, CMD, or any executable) and get stdout/stderr back directly. Supports inline commands and script files.

### Build (1 tool)

`msbuild` — Build .NET Framework projects (.csproj, .sln, .slnx) using MSBuild. Auto-discovers MSBuild.exe from the latest installed Visual Studio — no configuration needed. Supports Build, Rebuild, Clean, and Restore targets.

### SSH (5 tools)

Remote server access via SSH and SFTP. Credentials are stored in `mcp-config.json` profiles — never passed as tool parameters.

**Interactive Shell** — persistent, stateful remote sessions:

`ssh_open` · `ssh_send` · `ssh_close`

Open a connection with a named profile, send commands (state carries over between calls — `cd`, environment variables, etc.), and close when done.

**File Transfer** — one-shot SFTP operations (no `ssh_open` needed):

`ssh_upload` · `ssh_download`

Upload or download files and folders. Accepts a mix of file paths and directory paths — directories are transferred recursively. The connection is opened, used, and closed automatically per call.

---

## Design Decisions

### Two Editing Paradigms

MCP2 provides both **line-based** and **content-based** editing because each has strengths:

**Line-based** (`replace_line_range`, `batch_edit`, etc.) — Best for range operations: "delete lines 50–80", "replace lines 12–35 with this block". The `batch_edit` tool auto-sorts edits bottom-up to prevent line-shift corruption. The tradeoff: you need accurate line numbers, which go stale after edits.

**Content-based** (`replace_first`, `replace_all`, `edit_nth_occurrence`) — Best for targeted single-spot edits: "find this exact block and replace it with this". Immune to stale line numbers. `replace_first` with `must_be_unique=true` (the default) is the safest — it refuses to edit if the match is ambiguous.

Use content-based for precision. Use line-based for range work. Use `batch_edit` when you need multiple edits in one pass.

### MSBuild Auto-Discovery

The `msbuild` tool automatically finds MSBuild.exe by scanning `C:\Program Files\Microsoft Visual Studio\{version}\{edition}\MSBuild\Current\Bin\MSBuild.exe`. It picks the highest version number and checks Community, Professional, and Enterprise editions in that order. The discovered path is cached for the process lifetime — no configuration needed, and it works automatically when Visual Studio is upgraded.

### SSH Profile-Based Authentication

SSH credentials are stored in `mcp-config.json` under `ssh_profiles`, not passed as tool parameters. This keeps passwords and key paths out of the conversation. Each profile supports password authentication or private key authentication (with optional passphrase). The profile name doubles as the session identifier.

### Caller Validation

MCP2 validates that the calling process is Claude Desktop. Direct invocation from other processes is rejected. This is a built-in security boundary — not configurable.

### Automatic Backups

Every file edit creates a timestamped `.bak` file before making changes. This is on by default and can be disabled per-call with `create_backup: false`. Use `undo_last_edit` to restore instantly, or `clear_backups` to manage disk space.

---

## Architecture

```
MCP2/
├── Program.cs                    # Entry point, stdin/stdout JSON-RPC
├── McpServer.cs                  # MCP protocol handler
├── Core/
│   ├── ITool.cs                  # Tool interface (Name, Description, Params, Execute)
│   ├── ToolDiscovery.cs          # Auto-discovers all ITool implementations
│   ├── ToolResult.cs             # Standardized success/error responses
│   ├── McpConfig.cs              # Configuration loader (incl. SSH profiles)
│   ├── CallerValidator.cs        # Claude Desktop process validation
│   ├── JsonRpcModels.cs          # JSON-RPC 2.0 request/response models
│   └── McpModels.cs              # MCP protocol models
├── Services/
│   ├── FileOperations.cs         # Core file read/write/edit logic
│   ├── BackupService.cs          # Timestamped backup management
│   ├── MySqlService.cs           # MySQL connection and query execution
│   ├── HttpService.cs            # HTTP request handling
│   ├── SshSessionManager.cs      # Persistent SSH connection management
│   ├── SftpHelper.cs             # SFTP connection factory and utilities
│   └── MsBuildDiscovery.cs       # Auto-discovers MSBuild.exe from Visual Studio
└── Tools/                        # One class per tool, auto-discovered
    ├── File/                     # 13 tools: read, write, copy, move, etc.
    ├── FileEdit/                 # 12 tools: line-based and content-based editing
    ├── Directory/                # 7 tools: list, create, copy, move, delete
    ├── Search/                   # 2 tools: find_in_files, replace_in_files
    ├── Backup/                   # 6 tools: backup, undo, compare, diff
    ├── BatchRead/                # 2 tools: batch file reading
    ├── MySql/                    # 8 tools: queries, schema, batch operations
    ├── Http/                     # 3 tools: GET, POST, generic request
    ├── Zip/                      # 5 tools: create, extract, list archives
    ├── Image/                    # 1 tool: view_image (base64)
    ├── Shell/                    # 1 tool: run_command
    ├── Build/                    # 1 tool: msbuild (auto-discovers Visual Studio)
    └── Ssh/                      # 5 tools: ssh_open, ssh_send, ssh_close, ssh_upload, ssh_download
```

Adding a new tool: implement `ITool` in a new `.cs` file under `Tools/`. It's auto-discovered at startup — no registration needed.

---

## System Prompt

The `system-prompts.txt` file contains the complete tool reference documentation. You can paste it into Claude Desktop's system prompt (or a Claude Project's instructions) to give Claude full awareness of all tools, their parameters, and usage patterns.

---

## Dependencies

| Package | Purpose |
|---------|---------|
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/) | JSON-RPC protocol parsing |
| [MySqlConnector](https://www.nuget.org/packages/MySqlConnector/) | MySQL database access |
| [SSH.NET](https://www.nuget.org/packages/SSH.NET/) | SSH and SFTP remote access |

---

## License

[The Unlicense](https://unlicense.org/) — public domain. Use however you want.
