using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace a range of lines (start_line through end_line inclusive) with new content.
    /// If end_line is omitted, replaces just the single start_line.
    /// </summary>
    public class ReplaceLines : ITool
    {
        public string Name => "replace_lines";

        public string Description =>
            "[Line-targeted] Replace a range of lines (start_line through end_line, inclusive) with new content. " +
            "If end_line is omitted, defaults to start_line — equivalent to replace_line for a single line. " +
            "The new content can be more or fewer lines than the original range. " +
            "Returns a unified diff of the change. " +
            "WARNING: line numbers shift after any edit that adds or removes lines. For sequential edits to the same file, prefer replace_string (content-matched, self-verifying) or batch_edit_lines.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "First line of the range to replace (1-based, inclusive)", required: true)
            .Int("end_line", "Last line of the range to replace (1-based, inclusive). If omitted, defaults to start_line.")
            .String("content", "Replacement content. Can be more or fewer lines than the original range.", required: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            int? startLineNullable = args.Value<int?>("start_line");
            int? endLineNullable = args.Value<int?>("end_line");
            string content = args.Value<string>("content") ?? "";
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

            string note = FileOperations.EditLineRange(path, startLine, endLine, content);

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            string range = startLine == endLine
                ? $"line {startLine}"
                : $"lines {startLine}-{endLine}";
            string headline = note ?? $"Replaced {range}.";

            StringBuilder result = new StringBuilder();
            result.AppendLine(headline);
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
