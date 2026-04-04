using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class ReadLineRange : ITool
    {
        public string Name => "read_line_range";
        public string Description => "Read a specific range of lines from a file with line numbers. Out-of-range clamps gracefully: start_line beyond file returns info message, end_line clamps to actual length.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Int("start_line", "Starting line number (1-based)", required: true)
            .Int("end_line", "Ending line number (1-based)", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            int startLine = args.Value<int?>("start_line") ?? 1;
            int endLine = args.Value<int?>("end_line") ?? int.MaxValue;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (startLine < 1) startLine = 1;

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            string content = FileOperations.ReadLineRange(path, startLine, endLine);
            return ToolResult.Success(content);
        }
    }
}