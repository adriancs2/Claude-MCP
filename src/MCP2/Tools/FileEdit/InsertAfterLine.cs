using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Insert content after a specific 1-based line. Use line=0 to insert at the very top of the file.
    /// </summary>
    public class InsertAfterLine : ITool
    {
        public string Name => "insert_after_line";

        public string Description =>
            "[Line-targeted] Insert content after the given 1-based line — the new content becomes the next line(s) and the rest of the file shifts down. " +
            "Use line=0 to insert at the very top of the file (before line 1). " +
            "If line is beyond the end of the file, the content is appended. " +
            "Returns a unified diff of the insertion. " +
            "WARNING: line numbers shift after any edit that adds or removes lines. For sequential inserts at content-anchored locations, prefer replace_string with old_string=anchor and new_string=anchor+inserted_text.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("line", "Line number after which to insert (1-based). Use 0 to insert at the very top of the file.", required: true)
            .String("content", "Content to insert. Can be a single line or multiple lines (separated by \\n).", required: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            int? lineNullable = args.Value<int?>("line");
            string content = args.Value<string>("content") ?? "";
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (!lineNullable.HasValue || lineNullable.Value < 0)
                return ToolResult.Error("INVALID_PARAMS", "'line' must be >= 0 (use 0 to insert at the top of the file)");

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

            string note;
            string defaultHeadline;
            if (line == 0)
            {
                // Insert before line 1 = insert at top
                note = FileOperations.InsertAtLine(path, 1, content);
                defaultHeadline = "Inserted content at the top of the file (before line 1).";
            }
            else
            {
                note = FileOperations.InsertAfterLine(path, line, content);
                defaultHeadline = $"Inserted content after line {line}.";
            }

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            StringBuilder result = new StringBuilder();
            result.AppendLine(note ?? defaultHeadline);
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
