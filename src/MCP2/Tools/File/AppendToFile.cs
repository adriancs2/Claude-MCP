using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class AppendToFile : ITool
    {
        public string Name => "append_to_file";
        public string Description => "Append content to the end of an existing file.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("content", "Content to append to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string content = args.Value<string>("content");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (content == null)
                return ToolResult.Error("INVALID_PARAMS", "Missing 'content' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            FileOperations.AppendToFile(path, content);
            
            return ToolResult.Success($"Content appended successfully to: {path}");
        }
    }
}