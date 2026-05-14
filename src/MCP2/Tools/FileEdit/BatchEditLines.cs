using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Apply multiple line-targeted edits across one or more files in a single call.
    /// Internally sorts edits bottom-up per file so line numbers stay valid throughout
    /// — meaning all line numbers in the request are interpreted against the ORIGINAL
    /// file as last viewed, not against intermediate states.
    /// </summary>
    public class BatchEditLines : ITool
    {
        public string Name => "batch_edit_lines";

        public string Description =>
            "[Line-targeted, batched] Apply multiple line-based edits to one or more files in a single call. " +
            "All edits in the request are interpreted against the file's CURRENT state at call time — the tool internally sorts edits bottom-up per file so line numbers stay valid as edits apply. " +
            "Supported edit types: 'replace' (replace start_line through end_line, end_line optional), 'insert_after' (insert after the given line; use line=0 for top-of-file), 'delete' (delete start_line through end_line, end_line optional). " +
            "One backup is created per unique file (not per edit). " +
            "Returns one consolidated unified diff per modified file after all edits in the batch have been applied. " +
            "Use this when you've planned several line-based edits to one file from a single read — it's the safe way to do them together. " +
            "Each edit object: { 'file': required, 'type': 'replace'|'insert_after'|'delete', 'start_line' OR 'line': required (use 'line' for insert_after, 'start_line' for replace/delete), 'end_line': optional (replace/delete only), 'content': required for replace and insert_after }.";

        public ToolParamList Params => new ToolParamList()
            .Array("edits", "Array of edit objects. Each: {file, type, start_line or line, end_line (optional), content (for replace/insert_after)}", required: true)
            .Bool("create_backup", "Create one timestamped backup per unique file before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            var editsArray = args["edits"] as JArray;
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (editsArray == null || editsArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'edits' array");

            // ---- Step 1: parse and validate every edit up front ----
            var operations = new List<EditOp>();

            for (int i = 0; i < editsArray.Count; i++)
            {
                if (!(editsArray[i] is JObject editObj))
                    return ToolResult.Error("INVALID_PARAMS", $"edits[{i}] is not an object");

                string file = editObj.Value<string>("file");
                string type = editObj.Value<string>("type");
                // Accept either 'line' or 'start_line' as the primary line. 'line' reads more naturally for insert_after.
                int? startLine = editObj["start_line"]?.Value<int>() ?? editObj["line"]?.Value<int>();
                int? endLine = editObj["end_line"]?.Value<int>();
                string content = editObj.Value<string>("content");

                if (string.IsNullOrEmpty(file))
                    return ToolResult.Error("INVALID_PARAMS", $"edits[{i}] missing 'file'");
                if (string.IsNullOrEmpty(type))
                    return ToolResult.Error("INVALID_PARAMS", $"edits[{i}] missing 'type'");

                string typeLower = type.ToLowerInvariant();
                if (typeLower != "replace" && typeLower != "insert_after" && typeLower != "delete")
                    return ToolResult.Error("INVALID_PARAMS",
                        $"edits[{i}]: unknown type '{type}'. Must be 'replace', 'insert_after', or 'delete'.");

                // line semantics per type
                if (typeLower == "insert_after")
                {
                    if (!startLine.HasValue || startLine.Value < 0)
                        return ToolResult.Error("INVALID_PARAMS",
                            $"edits[{i}]: insert_after requires 'line' >= 0 (use 0 for top of file)");
                    if (content == null)
                        return ToolResult.Error("INVALID_PARAMS",
                            $"edits[{i}]: insert_after requires 'content'");
                }
                else // replace, delete
                {
                    if (!startLine.HasValue || startLine.Value < 1)
                        return ToolResult.Error("INVALID_PARAMS",
                            $"edits[{i}]: {typeLower} requires 'start_line' >= 1");
                    if (endLine.HasValue && endLine.Value < startLine.Value)
                        return ToolResult.Error("INVALID_PARAMS",
                            $"edits[{i}]: end_line must be >= start_line");
                    if (typeLower == "replace" && content == null)
                        return ToolResult.Error("INVALID_PARAMS",
                            $"edits[{i}]: replace requires 'content'");
                }

                if (!System.IO.File.Exists(file))
                    return ToolResult.Error($"File not found: {file}");

                operations.Add(new EditOp
                {
                    OriginalIndex = i,
                    FilePath = file,
                    Type = typeLower,
                    StartLine = startLine.Value,
                    EndLine = endLine ?? startLine.Value,
                    Content = content ?? ""
                });
            }

            // ---- Step 2: sort bottom-up per file so applying edits doesn't invalidate later line numbers ----
            // For insert_after, "later" means "higher line numbers" — same sort works.
            var sorted = operations
                .OrderBy(op => op.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(op => op.StartLine)
                .ThenByDescending(op => op.OriginalIndex) // stable for ties
                .ToList();

            // ---- Step 3: snapshot "before" content for each unique file, then backup ----
            // We snapshot BEFORE writing backups (a backup is a copy, doesn't mutate the
            // source) but the order doesn't really matter — what matters is that we
            // capture the file content prior to any edit in this batch being applied.
            var uniqueFiles = sorted
                .Select(op => System.IO.Path.GetFullPath(op.FilePath))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var beforeSnapshots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var fullPath in uniqueFiles)
                beforeSnapshots[fullPath] = System.IO.File.ReadAllText(fullPath, Encoding.UTF8);

            if (createBackup)
            {
                var backupService = new BackupService();
                foreach (var fullPath in uniqueFiles)
                    backupService.CreateBackup(fullPath);
            }

            // ---- Step 4: execute ----
            var report = new StringBuilder();
            int fileCount = uniqueFiles.Count;
            report.AppendLine($"Batch edit: {sorted.Count} operation(s) across {fileCount} file(s)");
            report.AppendLine("Edits sorted bottom-up per file to preserve line numbers.");
            report.AppendLine();

            int successCount = 0;
            var errors = new List<string>();

            foreach (var op in sorted)
            {
                try
                {
                    string note = null;
                    string label;

                    switch (op.Type)
                    {
                        case "replace":
                            if (op.StartLine == op.EndLine)
                                note = FileOperations.EditLine(op.FilePath, op.StartLine, op.Content);
                            else
                                note = FileOperations.EditLineRange(op.FilePath, op.StartLine, op.EndLine, op.Content);
                            label = op.StartLine == op.EndLine
                                ? $"replace {System.IO.Path.GetFileName(op.FilePath)} line {op.StartLine}"
                                : $"replace {System.IO.Path.GetFileName(op.FilePath)} lines {op.StartLine}-{op.EndLine}";
                            break;

                        case "insert_after":
                            if (op.StartLine == 0)
                                note = FileOperations.InsertAtLine(op.FilePath, 1, op.Content);
                            else
                                note = FileOperations.InsertAfterLine(op.FilePath, op.StartLine, op.Content);
                            label = op.StartLine == 0
                                ? $"insert at top of {System.IO.Path.GetFileName(op.FilePath)}"
                                : $"insert after {System.IO.Path.GetFileName(op.FilePath)} line {op.StartLine}";
                            break;

                        case "delete":
                            note = FileOperations.DeleteLines(op.FilePath, op.StartLine, op.EndLine);
                            label = op.StartLine == op.EndLine
                                ? $"delete {System.IO.Path.GetFileName(op.FilePath)} line {op.StartLine}"
                                : $"delete {System.IO.Path.GetFileName(op.FilePath)} lines {op.StartLine}-{op.EndLine}";
                            break;

                        default:
                            label = "(unknown type)";
                            break;
                    }

                    successCount++;
                    if (note != null)
                        report.AppendLine($"  NOTE: {label}: {note}");
                    else
                        report.AppendLine($"  OK: {label}");
                }
                catch (Exception ex)
                {
                    errors.Add($"edits[{op.OriginalIndex}] ({op.Type} {System.IO.Path.GetFileName(op.FilePath)} line {op.StartLine}): {ex.Message}");
                }
            }

            report.AppendLine();
            report.AppendLine($"Completed: {successCount}/{sorted.Count} operations succeeded");

            if (errors.Count > 0)
            {
                report.AppendLine();
                report.AppendLine("Errors:");
                foreach (var err in errors)
                    report.AppendLine($"  FAIL: {err}");
            }

            // ---- Step 5: emit one consolidated unified diff per modified file ----
            report.AppendLine();
            report.AppendLine(new string('=', 60));
            report.AppendLine($"Diffs ({uniqueFiles.Count} file(s)):");
            report.AppendLine(new string('=', 60));

            foreach (var fullPath in uniqueFiles)
            {
                string before = beforeSnapshots[fullPath];
                string after = System.IO.File.ReadAllText(fullPath, Encoding.UTF8);
                string diff = UnifiedDiff.ForEdit(fullPath, before, after);
                report.AppendLine();
                report.Append(diff);
            }

            return errors.Count == 0
                ? ToolResult.Success(report.ToString())
                : ToolResult.Error("PARTIAL_SUCCESS", report.ToString());
        }

        private class EditOp
        {
            public int OriginalIndex;
            public string FilePath;
            public string Type;
            public int StartLine;
            public int EndLine;
            public string Content;
        }
    }
}
