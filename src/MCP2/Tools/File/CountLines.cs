using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class CountLines : ITool
    {
        public string Name => "count_lines";
        public string Description => "Count the total number of lines in a file. Returns -1 for binary files.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            int count = FileOperations.CountLines(path);
            
            if (count == -1)
                return ToolResult.Success("Binary file - line count not applicable");
            
            return ToolResult.Success($"Total lines: {count}");
        }
    }
}