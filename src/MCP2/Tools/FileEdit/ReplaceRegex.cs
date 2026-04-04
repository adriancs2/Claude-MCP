using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace text using a regular expression pattern
    /// </summary>
    public class ReplaceRegex : ITool
    {
        public string Name => "replace_regex";
        public string Description => "Replace text using a regular expression pattern. Supports $1, $2 capture groups in replacement string.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("regex_pattern", "Regular expression pattern", required: true)
            .String("replacement", "Replacement text (can use $1, $2, etc.)", required: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                string regexPattern = args.Value<string>("regex_pattern");
                string replacement = args.Value<string>("replacement");
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (string.IsNullOrEmpty(regexPattern))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'regex_pattern' parameter");
                if (replacement == null)
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'replacement' parameter");

                

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

                FileOperations.ReplaceRegex(path, regexPattern, replacement);
                
                return ToolResult.Success(string.Format(
                    "Regex replacement completed successfully.{0}", backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}