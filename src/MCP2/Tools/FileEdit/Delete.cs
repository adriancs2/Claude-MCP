using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Delete a range of lines from a file
    /// </summary>
    public class Delete : ITool
    {
        public string Name => "delete";
        public string Description => "Delete a range of lines from a file. Out-of-range start_line returns error; end_line clamps to file length. WARNING: Line numbers shift after edits — edit bottom-up or use batch_edit.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "Starting line number (1-based)", required: true)
            .Int("end_line", "Ending line number (1-based)", required: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                int startLine = args.Value<int?>("start_line") ?? 0;
                int endLine = args.Value<int?>("end_line") ?? 0;
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (startLine < 1)
                    return ToolResult.Error("INVALID_PARAMS", "start_line must be >= 1");
                if (endLine < 1)
                    return ToolResult.Error("INVALID_PARAMS", "end_line must be >= 1");
                if (endLine < startLine)
                    return ToolResult.Error("INVALID_PARAMS", "end_line must be >= start_line");

                

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

                string note = FileOperations.DeleteLines(path, startLine, endLine);

                if (note != null)
                    return ToolResult.Success(string.Format("{0}{1}", note, backupInfo));
                
                int lineCount = endLine - startLine + 1;
                return ToolResult.Success(string.Format(
                    "{0} line(s) deleted (lines {1}-{2}).{3}", 
                    lineCount, startLine, endLine, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}