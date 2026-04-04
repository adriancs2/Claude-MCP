using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace the Nth occurrence of text in a file.
    /// Supports both single-line and multi-line blocks.
    /// </summary>
    public class EditNthOccurrence : ITool
    {
        public string Name => "edit_nth_occurrence";
        public string Description => "Replace the Nth occurrence of text in a file. Supports both single-line text and multi-line blocks (content can span multiple lines). Use find_all_occurrences first to identify which occurrence number you need.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("find_text", "Text or multi-line block to find. Must match file content exactly (including whitespace and indentation).", required: true)
            .Int("occurrence", "Which occurrence to replace (1-based)", required: true)
            .String("replace_text", "Replacement text or multi-line block. Can be more or fewer lines than find_text.", required: true)
            .Bool("case_sensitive", "Case-sensitive search. Default: true", defaultValue: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                string findText = args.Value<string>("find_text");
                int occurrence = args.Value<int?>("occurrence") ?? 0;
                string replaceText = args.Value<string>("replace_text");
                bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (string.IsNullOrEmpty(findText))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'find_text' parameter");
                if (occurrence < 1)
                    return ToolResult.Error("INVALID_PARAMS", "occurrence must be >= 1");
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

                FileOperations.EditNthOccurrence(path, findText, occurrence, replaceText, caseSensitive);
                
                return ToolResult.Success(string.Format(
                    "Occurrence #{0} replaced.{1}", occurrence, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}
