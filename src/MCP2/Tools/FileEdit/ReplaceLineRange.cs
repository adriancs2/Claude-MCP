using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;

namespace MCP2.Tools.FileEdit
{
    /// <summary>
    /// Replace a range of lines with new content.
    /// The most common edit operation: "take lines N-M and replace with this new block."
    /// </summary>
    public class ReplaceLineRange : ITool
    {
        public string Name => "replace_line_range";
        public string Description => "Replace a range of lines (start_line through end_line) with new content. " +
            "The new content can be more or fewer lines than the original range. " +
            "This is the primary tool for replacing a block of code. Out-of-range start_line returns error; end_line clamps to file length. WARNING: Line numbers shift after edits — edit bottom-up or use batch_edit.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "First line to replace (1-based)", required: true)
            .Int("end_line", "Last line to replace (1-based)", required: true)
            .String("new_content", "Replacement content (can be multiple lines)", required: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            try
            {
                string path = args.Value<string>("path");
                int startLine = args.Value<int?>("start_line") ?? 0;
                int endLine = args.Value<int?>("end_line") ?? 0;
                string newContent = args.Value<string>("new_content");
                bool createBackup = args.Value<bool?>("create_backup") ?? true;

                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
                if (startLine < 1)
                    return ToolResult.Error("INVALID_PARAMS", "start_line must be >= 1");
                if (endLine < 1)
                    return ToolResult.Error("INVALID_PARAMS", "end_line must be >= 1");
                if (endLine < startLine)
                    return ToolResult.Error("INVALID_PARAMS", "end_line must be >= start_line");
                if (newContent == null)
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'new_content' parameter");

                

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

                int originalLines = endLine - startLine + 1;
                string note = FileOperations.EditLineRange(path, startLine, endLine, newContent);

                if (note != null)
                    return ToolResult.Success(string.Format("{0}{1}", note, backupInfo));

                // Count new lines inserted
                int newLineCount = newContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).Length;

                return ToolResult.Success(string.Format(
                    "Replaced lines {0}-{1} ({2} lines) with {3} new lines.{4}",
                    startLine, endLine, originalLines, newLineCount, backupInfo));
            }
            catch (Exception ex)
            {
                return ToolResult.Error(ex.Message);
            }
        }
    }
}