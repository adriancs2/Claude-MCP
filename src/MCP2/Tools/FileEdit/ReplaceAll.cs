using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace all occurrences of text in a file.
    /// Supports both single-line and multi-line blocks.
    /// </summary>
    public class ReplaceAll : ITool
    {
        public string Name => "replace_all";
        public string Description => "Replace all occurrences of text in a file. Supports both single-line text and multi-line blocks (content can span multiple lines). Reports the number of replacements made. Use replace_first with must_be_unique=true when you want to ensure exactly one match exists before replacing.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("find_text", "Text or multi-line block to find. Must match file content exactly (including whitespace and indentation).", required: true)
            .String("replace_text", "Replacement text or multi-line block. Can be more or fewer lines than find_text.", required: true)
            .Bool("case_sensitive", "Case-sensitive search. Default: true", defaultValue: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                string findText = args.Value<string>("find_text");
                string replaceText = args.Value<string>("replace_text");
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

                string backupInfo = "";
                if (createBackup)
                {
                    var backupService = new BackupService();
                    string backupPath = backupService.CreateBackup(path);
                    backupInfo = string.Format("\nBackup created: {0}", 
                        System.IO.Path.GetFileName(backupPath));
                }

                int count = FileOperations.ReplaceAllCounted(path, findText, replaceText, caseSensitive);

                if (count == 0)
                    return ToolResult.Success("No occurrences found. No changes made.");

                return ToolResult.Success(string.Format(
                    "Replaced {0} occurrence(s).{1}", count, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}
