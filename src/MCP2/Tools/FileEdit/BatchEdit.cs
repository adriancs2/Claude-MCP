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
    /// Perform multiple edits across one or more files in a single operation.
    /// Edits are automatically sorted bottom-up per file to preserve line numbers.
    /// </summary>
    public class BatchEdit : ITool
    {
        public string Name => "batch_edit";
        public string Description => "Perform multiple edits across one or more files in a single operation. " +
            "Edits are automatically sorted bottom-up per file to preserve line numbers. Only one backup is created per file. " +
            "Parameter 'edits': Array of edit operations. Each operation is an object with: " +
            "'file' (string, required): Full file path. " +
            "'type' (string, required): One of 'replace', 'insert_at', 'insert_after', 'delete'. " +
            "'line' (int): Target line number (1-based). Alias for 'start_line' — use whichever is clearer for the operation. " +
            "'start_line' (int): Same as 'line'. For 'replace' and 'delete', this is the first line of the range. " +
            "'end_line' (int, optional): End line for 'replace' and 'delete' range operations. If omitted, defaults to start_line. " +
            "'content' (string): New content. Required for 'replace', 'insert_at', 'insert_after'. Not used for 'delete'. " +
            "Example: {\"edits\": [{\"file\": \"C:\\\\code\\\\app.cs\", \"type\": \"replace\", \"start_line\": 5, \"content\": \"new text\"}, " +
            "{\"file\": \"C:\\\\code\\\\app.cs\", \"type\": \"insert_after\", \"line\": 20, \"content\": \"inserted line\"}]}";

        public ToolParamList Params => new ToolParamList()
            .Array("edits", "Array of edit operations. Each object requires: 'file' (path), 'type' (replace|insert_at|insert_after|delete), 'line' or 'start_line', optional 'end_line', and 'content' (except for delete)", required: true)
            .Bool("create_backup", "Create backup before editing (one per file)", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            var editsArray = args["edits"] as JArray;
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (editsArray == null || editsArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'edits' array");

            // Step 1: Parse all edit operations
            var operations = new List<EditOperation>();

            foreach (JObject editObj in editsArray)
            {
                string file = editObj.Value<string>("file");
                string type = editObj.Value<string>("type");
                // Accept both 'line' and 'start_line' — 'line' is preferred for insert_at/insert_after
                int? startLine = editObj["line"]?.Value<int>() ?? editObj["start_line"]?.Value<int>();
                int endLine = editObj["end_line"]?.Value<int>() ?? (startLine ?? 0);
                string content = editObj.Value<string>("content") ?? "";

                if (string.IsNullOrEmpty(file))
                    return ToolResult.Error("INVALID_PARAMS", "Edit missing 'file' field");
                if (string.IsNullOrEmpty(type))
                    return ToolResult.Error("INVALID_PARAMS", "Edit missing 'type' field");
                if (!startLine.HasValue || startLine.Value < 1)
                    return ToolResult.Error("INVALID_PARAMS", 
                        string.Format("Invalid or missing 'start_line' for file {0}", System.IO.Path.GetFileName(file)));

                string typeLower = type.ToLowerInvariant();
                if (typeLower != "replace" && typeLower != "insert_at" && typeLower != "insert_after" && typeLower != "delete")
                    return ToolResult.Error("INVALID_PARAMS", 
                        string.Format("Unknown edit type '{0}'. Must be: replace, insert_at, insert_after, delete", type));

                if (typeLower != "delete" && content == "")
                {
                    // content is technically empty string which is allowed — user might want to clear a line
                    // but we should ensure it was actually provided for non-delete operations
                    if (editObj["content"] == null)
                        return ToolResult.Error("INVALID_PARAMS", 
                            string.Format("Edit type '{0}' requires 'content' field for file {1}", type, System.IO.Path.GetFileName(file)));
                }

                if (!System.IO.File.Exists(file))
                    return ToolResult.Error(string.Format("File not found: {0}", file));

                operations.Add(new EditOperation
                {
                    FilePath = file,
                    Type = typeLower,
                    StartLine = startLine.Value,
                    EndLine = endLine,
                    Content = content
                });
            }

            if (operations.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "No edit operations provided");

            // Step 2: Sort bottom-up per file to preserve line numbers
            var sorted = operations
                .OrderBy(op => op.FilePath, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(op => op.StartLine)
                .ToList();

            // Step 3: Create backups (one per unique file)
            var backedUpFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var backupService = new BackupService();

            if (createBackup)
            {
                var uniqueFiles = sorted.Select(op => System.IO.Path.GetFullPath(op.FilePath).ToLowerInvariant()).Distinct();
                foreach (var file in uniqueFiles)
                {
                    // Find original-cased path
                    string originalPath = sorted.First(op => 
                        System.IO.Path.GetFullPath(op.FilePath).Equals(file, StringComparison.OrdinalIgnoreCase)).FilePath;
                    backupService.CreateBackup(originalPath);
                    backedUpFiles.Add(file);
                }
            }

            // Step 4: Execute edits
            var results = new StringBuilder();
            var groupedByFile = operations.GroupBy(op => System.IO.Path.GetFullPath(op.FilePath).ToLowerInvariant());
            results.AppendLine(string.Format("Batch edit: {0} operation(s) across {1} file(s)", operations.Count, groupedByFile.Count()));
            results.AppendLine("Edits sorted bottom-up to preserve line numbers.");
            results.AppendLine();

            int successCount = 0;
            var errors = new List<string>();

            foreach (var op in sorted)
            {
                try
                {
                    string note = null;

                    switch (op.Type)
                    {
                        case "replace":
                            if (op.StartLine == op.EndLine)
                                note = FileOperations.EditLine(op.FilePath, op.StartLine, op.Content);
                            else
                                note = FileOperations.EditLineRange(op.FilePath, op.StartLine, op.EndLine, op.Content);
                            break;

                        case "insert_at":
                            note = FileOperations.InsertAtLine(op.FilePath, op.StartLine, op.Content);
                            break;

                        case "insert_after":
                            note = FileOperations.InsertAfterLine(op.FilePath, op.StartLine, op.Content);
                            break;

                        case "delete":
                            note = FileOperations.DeleteLines(op.FilePath, op.StartLine, op.EndLine);
                            break;
                    }

                    successCount++;
                    string lineRange = op.EndLine != op.StartLine 
                        ? string.Format("{0}-{1}", op.StartLine, op.EndLine) 
                        : op.StartLine.ToString();

                    if (note != null)
                        results.AppendLine(string.Format("  NOTE: {0} {1} line {2}: {3}", 
                            op.Type, System.IO.Path.GetFileName(op.FilePath), lineRange, note));
                    else
                        results.AppendLine(string.Format("  OK: {0} {1} line {2}", 
                            op.Type, System.IO.Path.GetFileName(op.FilePath), lineRange));
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format("{0} {1} line {2}: {3}", 
                        op.Type, System.IO.Path.GetFileName(op.FilePath), op.StartLine, ex.Message));
                }
            }

            results.AppendLine();
            results.AppendLine(string.Format("Completed: {0}/{1} operations succeeded", successCount, sorted.Count));

            if (errors.Count > 0)
            {
                results.AppendLine();
                results.AppendLine("Errors:");
                foreach (var err in errors)
                    results.AppendLine(string.Format("  FAIL: {0}", err));
            }

            return errors.Count == 0
                ? ToolResult.Success(results.ToString())
                : ToolResult.Error("PARTIAL_SUCCESS", results.ToString());
        }

        /// <summary>
        /// Internal representation of a batch edit operation
        /// </summary>
        private class EditOperation
        {
            public string FilePath { get; set; }
            public string Type { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public string Content { get; set; }
        }
    }
}