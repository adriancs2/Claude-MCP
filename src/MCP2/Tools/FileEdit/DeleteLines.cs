using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Delete a range of lines (start_line through end_line inclusive) from a file.
    /// If end_line is omitted, deletes only the start_line.
    /// </summary>
    public class DeleteLines : ITool
    {
        public string Name => "delete_lines";

        public string Description =>
            "[Line-targeted] Delete a range of lines (start_line through end_line, inclusive). " +
            "If end_line is omitted, deletes only the single start_line. " +
            "Returns a unified diff of the deletion. " +
            "WARNING: line numbers shift after any edit that adds or removes lines. For sequential deletions, either delete in a single batch_edit_lines call (which handles ordering internally) or use replace_string with old_string=text_to_delete and new_string=\"\" (content-matched, self-verifying).";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "First line to delete (1-based, inclusive)", required: true)
            .Int("end_line", "Last line to delete (1-based, inclusive). If omitted, defaults to start_line (single line deletion).")
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            int? startLineNullable = args.Value<int?>("start_line");
            int? endLineNullable = args.Value<int?>("end_line");
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (!startLineNullable.HasValue || startLineNullable.Value < 1)
                return ToolResult.Error("INVALID_PARAMS", "'start_line' must be >= 1");

            int startLine = startLineNullable.Value;
            int endLine = endLineNullable ?? startLine;

            if (endLine < startLine)
                return ToolResult.Error("INVALID_PARAMS", "'end_line' must be >= 'start_line'");

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            if (createBackup)
            {
                var backupService = new BackupService();
                backupService.CreateBackup(path);
            }

            // Snapshot before edit.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            string note = FileOperations.DeleteLines(path, startLine, endLine);

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            int deletedCount = endLine - startLine + 1;
            string range = startLine == endLine
                ? $"line {startLine}"
                : $"lines {startLine}-{endLine} ({deletedCount} lines)";
            string headline = note ?? $"Deleted {range}.";

            StringBuilder result = new StringBuilder();
            result.AppendLine(headline);
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
