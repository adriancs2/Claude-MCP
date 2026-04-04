using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace the first occurrence of text in a file.
    /// Supports both single-line and multi-line blocks.
    /// </summary>
    public class ReplaceFirst : ITool
    {
        public string Name => "replace_first";
        public string Description => "Replace the first occurrence of text in a file. Supports both single-line text and multi-line blocks (content can span multiple lines). When must_be_unique is true (default), the find_text must appear exactly once in the file or the operation is rejected — this prevents accidental edits when the match is ambiguous. Set must_be_unique to false to replace just the first match when duplicates exist.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("find_text", "Text or multi-line block to find. Must match file content exactly (including whitespace and indentation). For multi-line blocks, include the full block as it appears in the file.", required: true)
            .String("replace_text", "Replacement text or multi-line block. Can be more or fewer lines than find_text.", required: true)
            .Bool("must_be_unique", "If true (default), find_text must appear exactly once in the file — rejects if 0 or 2+ matches found. Set to false to replace just the first occurrence even when duplicates exist.", defaultValue: true)
            .Bool("case_sensitive", "Case-sensitive search. Default: true", defaultValue: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                string findText = args.Value<string>("find_text");
                string replaceText = args.Value<string>("replace_text");
                bool mustBeUnique = args.Value<bool?>("must_be_unique") ?? true;
                bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (string.IsNullOrEmpty(findText))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'find_text' parameter");
                if (replaceText == null)
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'replace_text' parameter");

                

                if (!System.IO.File.Exists(path))
                    return ToolResult.Error(string.Format("File not found: {0}", path));

                // Count occurrences if uniqueness check is requested
                if (mustBeUnique)
                {
                    int count = FileOperations.CountOccurrences(path, findText, caseSensitive);
                    if (count == 0)
                        return ToolResult.Error("NOT_FOUND", 
                            string.Format("find_text not found in file. No changes made."));
                    if (count > 1)
                        return ToolResult.Error("MULTIPLE_MATCHES", 
                            string.Format("find_text matches {0} locations in the file. Use must_be_unique=false to replace just the first, or use replace_all to replace all, or make find_text more specific to match exactly one location.", count));
                }

                string backupInfo = "";
                if (createBackup)
                {
                    var backupService = new BackupService();
                    string backupPath = backupService.CreateBackup(path);
                    backupInfo = string.Format("\nBackup created: {0}", 
                        System.IO.Path.GetFileName(backupPath));
                }

                int lineNumber = FileOperations.ReplaceFirstCounted(path, findText, replaceText, caseSensitive);

                if (lineNumber == 0)
                    return ToolResult.Success("No occurrences found. No changes made.");

                return ToolResult.Success(string.Format(
                    "Replaced at line {0}.{1}", lineNumber, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}
