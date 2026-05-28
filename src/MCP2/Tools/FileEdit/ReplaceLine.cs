using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace a single line by 1-based line number.
    /// </summary>
    public class ReplaceLine : ITool
    {
        public string Name => "replace_line";

        public string Description =>
            "[Line-targeted] Replace a single line by 1-based line number. " +
            "Returns a unified diff of the change. " +
            "Best used as the first edit immediately after viewing the file — line numbers from a view are accurate at that moment. " +
            "WARNING: line numbers shift after any edit that adds or removes lines. For sequential edits to the same file, prefer replace_string (content-matched, self-verifying) or batch_edit_lines (which sorts edits bottom-up internally so line numbers stay valid within one call).";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("line", "Line number to replace (1-based)", required: true)
            .String("content", "New content for that line. Can contain newlines to expand a single line into multiple.", required: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            int? lineNullable = args.Value<int?>("line");
            string content = args.Value<string>("content") ?? "";
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (!lineNullable.HasValue || lineNullable.Value < 1)
                return ToolResult.Error("INVALID_PARAMS", "'line' must be >= 1");

            int line = lineNullable.Value;

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            if (createBackup)
            {
                var backupService = new BackupService();
                backupService.CreateBackup(path);
            }

            // Snapshot before edit.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            string note = FileOperations.EditLine(path, line, content);

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            string headline = note ?? $"Replaced line {line}.";
            StringBuilder result = new StringBuilder();
            result.AppendLine(headline);
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
