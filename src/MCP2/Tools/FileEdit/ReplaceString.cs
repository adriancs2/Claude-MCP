using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Content-matched replacement of a single occurrence. The preferred default
    /// edit tool — self-verifying, immune to line-number drift.
    /// </summary>
    public class ReplaceString : ITool
    {
        public string Name => "replace_string";

        public string Description =>
            "[Content-matched] Replace one occurrence of old_string with new_string in a file. " +
            "By default the match must be unique in the file — if old_string appears 0 or 2+ times the tool errors without making any change. " +
            "Returns the line number along with a unified diff of the change. " +
            "PREFERRED DEFAULT: this is the safest edit tool because it locates the change point by content, not by line number, " +
            "so it can't be thrown off by prior edits shifting line numbers. " +
            "Pass enough surrounding context in old_string to make the match unambiguous; otherwise either pad it further or use replace_string_nth. " +
            "Set must_be_unique=false to replace just the first match even when duplicates exist.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("old_string", "Text to find. Multi-line content is supported — match must be exact (whitespace, indentation, line endings).", required: true)
            .String("new_string", "Replacement text. Can be more or fewer lines than old_string.", required: true)
            .Bool("must_be_unique", "If true (default), old_string must appear exactly once — rejects on 0 or 2+ matches. Set false to replace just the first occurrence.", defaultValue: true)
            .Bool("case_sensitive", "Case-sensitive match", defaultValue: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string oldString = args.Value<string>("old_string");
            string newString = args.Value<string>("new_string") ?? "";
            bool mustBeUnique = args.Value<bool?>("must_be_unique") ?? true;
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(oldString))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'old_string' parameter");

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            // Uniqueness check
            int occurrences = FileOperations.CountOccurrences(path, oldString, caseSensitive);
            if (occurrences == 0)
                return ToolResult.Error("NOT_FOUND", "old_string not found in file. No changes made.");

            if (mustBeUnique && occurrences > 1)
                return ToolResult.Error("AMBIGUOUS_MATCH",
                    $"old_string appears {occurrences} times in the file but must_be_unique=true. " +
                    "Add more surrounding context to old_string to make it unique, or set must_be_unique=false to replace just the first match, or use replace_string_nth to target a specific occurrence.");

            // Backup
            if (createBackup)
            {
                var backupService = new BackupService();
                backupService.CreateBackup(path);
            }

            // Snapshot before applying, so we can diff afterward.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            // Apply
            int lineNumber = FileOperations.ReplaceFirstCounted(path, oldString, newString, caseSensitive);

            // Read post-edit content and build the diff.
            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            StringBuilder result = new StringBuilder();
            result.Append($"Replaced 1 occurrence at line {lineNumber}. ");
            result.AppendLine(mustBeUnique
                ? "(unique match)"
                : $"({occurrences - 1} other match(es) left untouched)");
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
