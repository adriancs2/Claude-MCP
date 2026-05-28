# Tool Reference

Every tool MCP2 exposes, grouped by category. Tool names match what Claude sees in the MCP listing.

For the *why* behind the design (two editing paradigms, content-vs-line tradeoffs, batch ordering, caller validation), see the [Architecture](Architecture) page. This page focuses on *what each tool does*.

---

## File Operations (11 tools)

Read, write, copy, move, delete files. Search for patterns with line numbers. Count lines. Check file existence and metadata.

`read_file` is a unified reader — pass `start_line` / `end_line` for ranged reads and `show_line_numbers` to prefix line numbers. Out-of-range line numbers clamp gracefully (an `end_line` beyond the file just stops at the last line; a `start_line` past the end returns an info message rather than erroring). Use it for full-file reads when you just want content, and pass `show_line_numbers: true` when you're planning a follow-up line-targeted edit so the numbers are already on screen.

`find_pattern` returns matching lines with line numbers — useful when you only need to know *where* matches are. `find_all_occurrences` returns the same matches with surrounding context lines, which is what you want before targeting a specific occurrence with `replace_string_nth`.

`write_file` creates or overwrites, returning a unified diff when overwriting an existing file. `append_to_file` adds to the end and returns a diff of what was appended.

`get_file_info` returns size, modified time, attributes, and encoding heuristics — handy for sanity-checking before reading something large.

> `read_file` · `count_lines` · `find_pattern` · `find_all_occurrences` · `write_file` · `append_to_file` · `copy_file` · `move_file` · `delete_file` · `file_exists` · `get_file_info`

---

## File Editing (9 tools)

Two parallel approaches — **line-based** and **content-based**. They aren't competing; they're suited to different jobs.

### Content-based (4 tools)

Locate the change point by *text*, not line number. Immune to line-shift drift from earlier edits. This is the safer default for most editing.

**`replace_string`** is the preferred starting point. By default `must_be_unique` is `true` — if `old_string` appears 0 or 2+ times in the file, the tool refuses and returns an `AMBIGUOUS_MATCH` error explaining how to recover (add more context to `old_string`, switch to `replace_string_nth`, or set `must_be_unique=false`). It supports multi-line blocks. The match must be exact, including whitespace, indentation, and line endings.

**`replace_string_all`** replaces every occurrence in a file. Use for in-file renames where every match should change — for example, renaming a local variable or updating a literal string used in several places.

**`replace_string_nth`** targets the Nth occurrence (1-based). Use when a string legitimately appears multiple times and you want exactly the kth one — for example, the 3rd `return null;` in a function. Pair it with `find_all_occurrences` to see all matches with their indices first. This is brittle if the file is being concurrently edited elsewhere in ways that add or remove earlier occurrences — prefer `replace_string` with extra surrounding context whenever feasible.

**`replace_string_regex`** uses .NET regex with full substitution support — `$1`, `$2`, `${name}` for capture groups, `$$` for a literal dollar sign. Useful for structural changes where a literal `old_string` would need too much surrounding context to disambiguate. Remember to escape regex metacharacters in literal text: `.` `*` `+` `?` `^` `$` `(` `)` `[` `]` `{` `}` `|` `\`.

> `replace_string` · `replace_string_all` · `replace_string_nth` · `replace_string_regex`

### Line-based (5 tools)

Best for range operations: "delete lines 50–80", "replace this 30-line block with a 12-line block", "insert this header at the top of the file".

**`replace_line`** replaces a single line by 1-based line number. Best used immediately after a `read_file` with `show_line_numbers: true` — line numbers are accurate at that exact moment. They go stale after any edit that adds or removes lines.

**`replace_lines`** replaces a range. The new content can be any number of lines, including more or fewer than the original range.

**`insert_after_line`** inserts content after the given line. Use `line=0` to insert at the top of the file.

**`delete_lines`** deletes a range (or a single line if you omit `end_line`).

**`batch_edit_lines`** is the workhorse for multi-edit sessions. You pass several edits referencing the file's *current* line numbers; internally the tool sorts them bottom-up before applying so line numbers stay valid throughout the batch. Supported edit types: `replace` (with `start_line` + optional `end_line`), `insert_after` (with `line`), and `delete` (with `start_line` + optional `end_line`). One backup per file regardless of edit count. You get back a single consolidated unified diff per modified file showing the cumulative effect of all edits.

> `replace_line` · `replace_lines` · `insert_after_line` · `delete_lines` · `batch_edit_lines`

---

## Directory Operations (6 tools)

`list_directory` supports glob patterns (`*`, `?`, `**`), multiple patterns separated by semicolons, recursive scans, sort by name or modified date, exclusion patterns, and a result limit. Pattern examples: `*.cs;*.aspx`, `src/**/test_*.py`. The `**` form auto-enables recursion. Common exclusions like `node_modules;bin;obj;.git` keep listings sane on real projects.

`batch_copy_files` copies multiple files in one call — either to a single destination directory (pass `dest_dir` plus a list of source paths) or with explicit `{source, destination}` mappings. Set `preserve_structure: true` to recreate the relative directory tree from a common parent.

The directory create/copy/move/delete tools are straightforward — full paths, parents created as needed for `create_directory`.

> `list_directory` · `create_directory` · `copy_directory` · `move_directory` · `delete_directory` · `batch_copy_files`

---

## Search Tools (2 tools)

Project-wide find-and-replace.

**`find_in_files`** searches for a pattern across all files in a directory, returning matches with file paths and line numbers. Supports file patterns (`*.cs`, `*.aspx`), recursion, and case sensitivity options.

**`replace_in_files`** does the same lookup and applies a replacement. Returns a unified diff per modified file. Backups are created per file by default.

Both support **presets** that auto-exclude common build artifacts:

- **`dotnet`** — skips `bin`, `obj`, `packages`, `.vs`, `.git`
- **`web`** — skips `node_modules`, `bower_components`, `dist`, `build`, `.git`
- **`python`** — skips `__pycache__`, `.venv`, `venv`, `.git`

You can also pass explicit `exclude_folders` (semicolon-separated) and `exclude_extensions` for one-off cases. Perfect for renaming CSS classes, updating namespaces, or any project-wide refactor.

> `find_in_files` · `replace_in_files`

---

## Backup & Diff (6 tools)

Every file-modifying tool creates a timestamped backup automatically — unless you pass `create_backup: false`. Backups land in `./backups` next to the exe by default (configurable via `backup_directory` in `mcp-config.json`). Naming pattern: `<filename>.<YYYYMMDD_HHMMSS>.bak`. Rapid successive edits append milliseconds to avoid collisions.

**`backup_file`** creates a manual backup without touching the original — useful before a risky multi-step refactor where you want a labeled checkpoint.

**`undo_last_edit`** restores a file from its most recent backup. One-call rollback.

**`list_backups`** shows all backups for a file, newest first, with sizes and timestamps.

**`clear_backups`** deletes backups older than a given number of days — useful for reclaiming disk space.

**`compare_files`** runs a real LCS-based diff between two files and returns unified-diff output with `@@` hunk headers. It aligns unchanged lines correctly: a one-line insertion at the top of a 600-line file produces one hunk, not 599 shifted lines. Supports `ignore_whitespace` (collapses runs of whitespace, trims line ends) and `ignore_case`. Configurable context lines (default 3).

**`diff_preview`** shows what a `replace_first`, `replace_all`, or `replace_regex` operation *would* do, as a unified diff, without applying the change. Useful for dry-running a project-wide find-and-replace before committing.

> `backup_file` · `undo_last_edit` · `list_backups` · `clear_backups` · `compare_files` · `diff_preview`

---

## Batch Read (1 tool)

**`batch_read_files`** reads multiple files in a single call. Each entry in the `files` array is either a plain string (full path → full file) or an object: `{path, start_line?, end_line?, label?}` for ranged reads.

Three output formats:

- `separated` (default) — one block per file with prominent headers
- `combined` — lightweight per-file headers (compact for many files)
- `minimal` — no headers, contents back-to-back

Use `show_line_numbers: true` for full-file reads to prefix every line with its number; ranged reads always include line numbers regardless.

This is the right tool when you need context across several files — for example, reading a test file, the file under test, and the shared utility module before deciding on an edit.

> `batch_read_files`

---

## MySQL Database (8 tools)

Full database access using the connection string from `mcp-config.json`. The tools are split by result shape, which keeps responses small and lets Claude pick the right one.

**`mysql_select`** runs queries that return result sets — `SELECT`, `SHOW`, `DESCRIBE`, `EXPLAIN`. Returns rows.

**`mysql_execute`** runs statements that don't return result sets — `INSERT`, `UPDATE`, `DELETE`, `CREATE`, `DROP`, `ALTER`. Returns affected-row count.

**`mysql_scalar`** runs a query that returns a single value — `COUNT(*)`, `MAX(...)`, `MIN(...)`, `EXISTS(...)`. Returns one value, not a row.

**`mysql_schema`** returns schema info, switched by `info_type`: `databases`, `tables`, `columns`, or `create_table`. One tool, four queries.

**`mysql_test`** verifies the connection string works. Returns server version and current user. Run this once after editing the config.

**`batch_mysql_schema`** pulls structure for multiple tables in one call — useful when Claude is reading several related tables before composing a query.

**`batch_mysql_queries`** runs a sequence of queries in one call. Each query's result is returned independently.

**`batch_mysql_queries_with_variables`** is the interesting one. Queries in the batch share session state — `SET @last_id := LAST_INSERT_ID()` in step 1 is available in step 2. Useful for multi-step inserts where a FK depends on the new PK without an extra round-trip from Claude.

> `mysql_execute` · `mysql_select` · `mysql_scalar` · `mysql_schema` · `mysql_test` · `batch_mysql_schema` · `batch_mysql_queries` · `batch_mysql_queries_with_variables`

---

## HTTP (3 tools)

**`http_get`** — simple GET with optional headers.

**`http_post`** — POST with form data or a JSON body.

**`http_request`** — generic request with any method (`GET`, `POST`, `PUT`, `DELETE`, `PATCH`, etc.), custom headers, and body. Use this when you need full control.

All three return status, headers, and body. Useful for API exploration, hitting local dev servers, and fetching JSON for analysis.

> `http_get` · `http_post` · `http_request`

---

## Zip (5 tools)

**`zip_file`** — create a new ZIP archive or add files to an existing one.

**`zip_folder`** — pack an entire folder into a ZIP.

**`extract_zip`** — extract everything to a destination folder.

**`extract_zip_content`** — extract only files matching one or more patterns (e.g. `["*.dll", "lib/net48/**"]`). Useful for grabbing specific assemblies out of a NuGet package without unpacking the whole thing.

**`list_zip_contents`** — list every file inside a ZIP with sizes, optionally filtered by pattern. No extraction — just inspection.

> `zip_file` · `zip_folder` · `extract_zip` · `extract_zip_content` · `list_zip_contents`

---

## Image (1 tool)

**`view_image`** reads an image file from disk and returns it as base64 so Claude can analyze it visually. Use this when Claude needs to look at a screenshot, diagram, photo, or anything else that's on disk rather than already in the conversation.

> `view_image`

---

## Shell (1 tool)

**`run_command`** executes any external program — PowerShell, cmd, an executable, a batch file — and returns stdout, stderr, and exit code. Supports inline commands and script files. Useful for one-off tasks that don't deserve their own tool: running `git status`, kicking off a Python script, invoking a CLI utility.

> `run_command`

---

## Build (1 tool)

**`msbuild`** builds .NET Framework projects — `.csproj`, `.sln`, `.slnx` — using MSBuild from the latest installed Visual Studio. Auto-discovers `MSBuild.exe` by scanning `C:\Program Files\Microsoft Visual Studio\{version}\{edition}\MSBuild\Current\Bin\MSBuild.exe`, picking the highest version, and checking Community → Professional → Enterprise in that order. The discovered path is cached for the process lifetime.

Supports targets: `Build`, `Rebuild`, `Clean`, `Restore`. Configurations: `Debug`, `Release`. Verbosity: `quiet`, `minimal`, `normal`, `detailed`, `diagnostic`.

Output is post-processed: **errors are always shown**, **warnings are hidden by default** and replaced with a count summary. Pass `show_warnings: true` to see them expanded. The warning-line regex handles 4-tuple spans (VS 2022+), `N>` multi-proc prefixes, and `MSBUILD : warning MSBxxxx` lines.

`timeout_seconds` defaults to 120 — bump it for large solutions.

> `msbuild`

---

## SSH (5 tools)

Remote server access via SSH and SFTP. Credentials are stored in `mcp-config.json` under `ssh_profiles` — never passed as tool parameters, never visible in the conversation transcript.

### Interactive shell (3 tools)

**`ssh_open`** opens a persistent connection using a named profile. The profile name doubles as the session identifier.

**`ssh_send`** issues a command on an open session. State carries across calls: `cd /var/www`, `export FOO=bar`, environment variables, shell history — all preserved.

**`ssh_close`** ends the session and releases resources.

### One-shot file transfer (2 tools)

**`ssh_upload`** and **`ssh_download`** skip the open/close dance — pass a mix of file and directory paths (directories transfer recursively), and the SFTP connection opens, transfers, and closes in a single call. No `ssh_open` needed first.

`ssh_download` writes to a local destination folder; missing folders are created. Both default to `overwrite: true`.

> `ssh_open` · `ssh_send` · `ssh_close` · `ssh_upload` · `ssh_download`

---

## Total: 59 tools

| Category | Count |
|---|---|
| File operations | 11 |
| File editing | 9 |
| Directory operations | 6 |
| Search | 2 |
| Backup & diff | 6 |
| Batch read | 1 |
| MySQL | 8 |
| HTTP | 3 |
| Zip | 5 |
| Image | 1 |
| Shell | 1 |
| Build | 1 |
| SSH | 5 |
| **Total** | **59** |
