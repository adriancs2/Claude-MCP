using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class ReadFileLines : ITool
    {
        public string Name => "read_file_lines";
        public string Description => "Read file content with line numbers prefixed to each line. " +
            "Optionally specify start_line and end_line to read only a specific range.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "Starting line number (1-based, optional). If omitted, reads from beginning.")
            .Int("end_line", "Ending line number (1-based, optional). If omitted, reads to end of file.");

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            int? startLine = args.Value<int?>("start_line");
            int? endLine = args.Value<int?>("end_line");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            // If any range parameter specified, use ReadLineRange (handles out-of-range gracefully)
            if (startLine.HasValue || endLine.HasValue)
            {
                int start = startLine ?? 1;
                int end = endLine ?? int.MaxValue; // ReadLineRange will clamp to actual file length

                if (start < 1) start = 1;

                string rangeContent = FileOperations.ReadLineRange(path, start, end);
                return ToolResult.Success(rangeContent);
            }

            // No range specified — read entire file
            string content = FileOperations.ReadFileWithLineNumbers(path);
            return ToolResult.Success(content);
        }
    }
}