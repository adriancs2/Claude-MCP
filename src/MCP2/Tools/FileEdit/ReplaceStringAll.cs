using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Content-matched replacement of every occurrence in a file.
    /// </summary>
    public class ReplaceStringAll : ITool
    {
        public string Name => "replace_string_all";

        public string Description =>
            "[Content-matched] Replace every occurrence of old_string with new_string in a file. " +
            "Returns the number of replacements made along with a unified diff of the changes. " +
            "Use when you intend to update every match (e.g., renaming an identifier project-wide within one file). " +
            "If you want to replace only one match, use replace_string instead — that tool errors when matches are ambiguous, which is safer.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("old_string", "Text to find. Multi-line content is supported.", required: true)
            .String("new_string", "Replacement text", required: true)
            .Bool("case_sensitive", "Case-sensitive match", defaultValue: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string oldString = args.Value<string>("old_string");
            string newString = args.Value<string>("new_string") ?? "";
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(oldString))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'old_string' parameter");

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            if (createBackup)
            {
                var backupService = new BackupService();
                backupService.CreateBackup(path);
            }

            // Snapshot the file BEFORE applying the edit so we can diff against
            // the result. We read raw bytes here; UnifiedDiff handles
            // line-ending normalization internally, so any \r\n vs \n
            // differences won't show as noise in the diff.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            int count = FileOperations.ReplaceAllCounted(path, oldString, newString, caseSensitive);

            if (count == 0)
                return ToolResult.Error("NOT_FOUND", "old_string not found in file. No changes made.");

            // Read the post-edit content and compute the diff.
            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            StringBuilder result = new StringBuilder();
            result.AppendLine($"Replaced {count} occurrence(s).");
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
