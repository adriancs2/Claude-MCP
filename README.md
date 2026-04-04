# MCP2 — Enhanced MCP Tool Server for Claude Desktop

A unified MCP (Model Context Protocol) server that gives Claude Desktop 60 tools for file operations, code editing, shell commands, database access, document processing, HTTP requests, and more. Built as a single .NET Framework 4.8 console application.

![.NET Framework](https://img.shields.io/badge/.NET%20Framework-4.8-purple)
![C#](https://img.shields.io/badge/C%23-7.3-blue)
![License](https://img.shields.io/badge/license-Unlicense-green)
![Tools](https://img.shields.io/badge/tools-69-orange)

> **Note:** This is designed for **Claude Desktop** on Windows. Claude Code already has built-in tools that cover most of this functionality — though if you use Claude Code and need MySQL tools, you can extract that section from this project.

---

## Documentation - How to Build MCP in C# Console App

- [Documentation: Writing MCP Tools in C# (.NET Framework) for Claude Desktop/Code
](https://adriancs.com/documentation-writing-mcp-tools-in-c-net-framework-for-claude-desktop-code/)
- [Building a Web Server from Scratch in C#
](https://adriancs.com/building-a-web-server-from-scratch-in-csharp/)

---

## Why This Exists

Claude Desktop's built-in file tools are limited. You can't:

- Edit a specific line by number
- Target the 3rd occurrence of a pattern when there are 10 matches
- Make multiple edits to a file without line numbers shifting
- Replace a block of code with content-matching safety (like `str_replace`, but better)
- Execute MySQL queries
- Run shell commands and get the output back

MCP2 solves all of that with 60 tools in a single executable.

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
  "backup_directory": null
}
```

All fields are optional. If you don't use MySQL, leave the connection string empty.

| Setting | Description | Default |
|---------|-------------|---------|
| `mysql_connection_string` | MySQL connection string | (empty) |
| `gc_memory_threshold_mb` | Memory threshold to trigger garbage collection | 150 |
| `debug_logging` | Write debug log to `mcp_debug.log` | false |
| `backup_directory` | Custom path for backup files | `./backups` next to exe |

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

## What's In the Box (60 Tools)

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

---

## Design Decisions

### Two Editing Paradigms

MCP2 provides both **line-based** and **content-based** editing because each has strengths:

**Line-based** (`replace_line_range`, `batch_edit`, etc.) — Best for range operations: "delete lines 50–80", "replace lines 12–35 with this block". The `batch_edit` tool auto-sorts edits bottom-up to prevent line-shift corruption. The tradeoff: you need accurate line numbers, which go stale after edits.

**Content-based** (`replace_first`, `replace_all`, `edit_nth_occurrence`) — Best for targeted single-spot edits: "find this exact block and replace it with this". Immune to stale line numbers. `replace_first` with `must_be_unique=true` (the default) is the safest — it refuses to edit if the match is ambiguous.

Use content-based for precision. Use line-based for range work. Use `batch_edit` when you need multiple edits in one pass.

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
│   ├── McpConfig.cs              # Configuration loader
│   ├── CallerValidator.cs        # Claude Desktop process validation
│   ├── JsonRpcModels.cs          # JSON-RPC 2.0 request/response models
│   └── McpModels.cs              # MCP protocol models
├── Services/
│   ├── FileOperations.cs         # Core file read/write/edit logic
│   ├── BackupService.cs          # Timestamped backup management
│   ├── MySqlService.cs           # MySQL connection and query execution
│   └── HttpService.cs            # HTTP request handling
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
    └── Shell/                    # 1 tool: run_command
```

Adding a new tool: implement `ITool` in a new `.cs` file under `Tools/`. It's auto-discovered at startup — no registration needed.

---

## System Prompt

The `system-prompts.txt` file contains the complete tool reference documentation. You can paste it into Claude Desktop's system prompt (or a Claude Project's instructions) to give Claude full awareness of all 69 tools, their parameters, and usage patterns.

---

## Dependencies

| Package | Purpose |
|---------|---------|
| [Newtonsoft.Json](https://www.nuget.org/packages/Newtonsoft.Json/) | JSON-RPC protocol parsing |
| [MySqlConnector](https://www.nuget.org/packages/MySqlConnector/) | MySQL database access |

---

## License

[The Unlicense](https://unlicense.org/) — public domain. Use however you want.