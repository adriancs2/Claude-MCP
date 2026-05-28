using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Content-matched replacement of a specific Nth occurrence.
    /// </summary>
    public class ReplaceStringNth : ITool
    {
        public string Name => "replace_string_nth";

        public string Description =>
            "[Content-matched] Replace the Nth occurrence (1-based) of old_string with new_string. " +
            "Returns a unified diff of the change. " +
            "Use when old_string legitimately appears multiple times and you need exactly the kth one — for example, the 3rd 'return null;' in a file. " +
            "Brittle if the file is also being edited in ways that add/remove earlier occurrences (the 'N' shifts). " +
            "Prefer replace_string with extra surrounding context whenever possible — that's safer than counting.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("old_string", "Text to find", required: true)
            .String("new_string", "Replacement text", required: true)
            .Int("n", "Which occurrence to replace (1-based: 1 = first, 2 = second, ...)", required: true)
            .Bool("case_sensitive", "Case-sensitive match", defaultValue: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string oldString = args.Value<string>("old_string");
            string newString = args.Value<string>("new_string") ?? "";
            int? nNullable = args.Value<int?>("n");
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(oldString))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'old_string' parameter");
            if (!nNullable.HasValue || nNullable.Value < 1)
                return ToolResult.Error("INVALID_PARAMS", "'n' must be >= 1");

            int n = nNullable.Value;

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            if (createBackup)
            {
                var backupService = new BackupService();
                backupService.CreateBackup(path);
            }

            // Snapshot before the edit so we can diff afterward.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            try
            {
                FileOperations.EditNthOccurrence(path, oldString, n, newString, caseSensitive);
            }
            catch (System.InvalidOperationException ex)
            {
                return ToolResult.Error("NOT_ENOUGH_OCCURRENCES", ex.Message);
            }

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            StringBuilder result = new StringBuilder();
            result.AppendLine($"Replaced occurrence #{n} of old_string.");
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
