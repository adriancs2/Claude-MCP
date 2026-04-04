using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Insert content before a specific line
    /// </summary>
    public class InsertAt : ITool
    {
        public string Name => "insert_at";
        public string Description => "Insert content before a specific line. Out-of-range line appends to end. WARNING: Line numbers shift after edits — edit bottom-up or use batch_edit.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("line_number", "Line number to insert before (1-based)", required: true)
            .String("content", "Content to insert", required: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                int lineNumber = args.Value<int?>("line_number") ?? 0;
                string content = args.Value<string>("content");
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (lineNumber < 1)
                    return ToolResult.Error("INVALID_PARAMS", "line_number must be >= 1");
                if (content == null)
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'content' parameter");

                

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

                string note = FileOperations.InsertAtLine(path, lineNumber, content);

                if (note != null)
                    return ToolResult.Success(string.Format("{0}{1}", note, backupInfo));
                
                return ToolResult.Success(string.Format(
                    "Content inserted before line {0}.{1}", lineNumber, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}