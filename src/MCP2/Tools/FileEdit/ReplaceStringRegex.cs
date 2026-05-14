using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Regex-based content replacement.
    /// </summary>
    public class ReplaceStringRegex : ITool
    {
        public string Name => "replace_string_regex";

        public string Description =>
            "[Content-matched] Replace text using a .NET regular-expression pattern. " +
            "All matches in the file are replaced. The replacement string supports standard substitutions: $1, $2, ${name} for capture groups, $$ for a literal dollar sign. " +
            "Returns a unified diff of the changes. " +
            "Use this when the change is structural (e.g., update every method signature matching a pattern) and a literal old_string would need too much surrounding context to disambiguate. " +
            "For literal-string matching, prefer replace_string or replace_string_all — they don't require regex escaping.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("pattern", ".NET regex pattern. Remember to escape regex metacharacters in literal text: . * + ? ^ $ ( ) [ ] { } | \\", required: true)
            .String("replacement", "Replacement text. Supports $1, $2, ${name}, $$ substitutions.", required: true)
            .Bool("create_backup", "Create timestamped backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string pattern = args.Value<string>("pattern");
            string replacement = args.Value<string>("replacement") ?? "";
            bool createBackup = args.Value<bool?>("create_backup") ?? true;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(pattern))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'pattern' parameter");

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            if (createBackup)
            {
                var backupService = new BackupService();
                backupService.CreateBackup(path);
            }

            // Snapshot before the regex replace so we can diff afterward.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            try
            {
                FileOperations.ReplaceRegex(path, pattern, replacement);
            }
            catch (System.ArgumentException ex)
            {
                return ToolResult.Error("INVALID_REGEX", ex.Message);
            }

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            StringBuilder result = new StringBuilder();
            result.AppendLine($"Regex replacement applied with pattern: {pattern}");
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
