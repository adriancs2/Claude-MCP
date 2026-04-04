using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class ReadFile : ITool
    {
        public string Name => "read_file";
        public string Description => "Read the complete raw content of a file without line numbers.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            string content = FileOperations.ReadFile(path);
            return ToolResult.Success(content);
        }
    }
}