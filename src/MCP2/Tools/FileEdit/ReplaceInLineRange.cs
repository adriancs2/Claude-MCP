using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace text within a specific line range
    /// </summary>
    public class ReplaceInLineRange : ITool
    {
        public string Name => "replace_in_line_range";
        public string Description => "Find-and-replace text within a specific line range (text match within range, not full line replacement).";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "Starting line number (1-based)", required: true)
            .Int("end_line", "Ending line number (1-based)", required: true)
            .String("find_text", "Text to find", required: true)
            .String("replace_text", "Replacement text", required: true)
            .Bool("case_sensitive", "Case-sensitive search", defaultValue: false)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                int startLine = args.Value<int?>("start_line") ?? 0;
                int endLine = args.Value<int?>("end_line") ?? 0;
                string findText = args.Value<string>("find_text");
                string replaceText = args.Value<string>("replace_text");
                bool caseSensitive = args.Value<bool?>("case_sensitive") ?? false;
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (startLine < 1)
                    return ToolResult.Error("INVALID_PARAMS", "start_line must be >= 1");
                if (endLine < 1)
                    return ToolResult.Error("INVALID_PARAMS", "end_line must be >= 1");
                if (endLine < startLine)
                    return ToolResult.Error("INVALID_PARAMS", "end_line must be >= start_line");
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

                FileOperations.ReplaceInLineRange(path, startLine, endLine, findText, replaceText, caseSensitive);
                
                return ToolResult.Success(string.Format(
                    "Text replaced in lines {0}-{1}.{2}", startLine, endLine, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}