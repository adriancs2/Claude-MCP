using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class FileExists : ITool
    {
        public string Name => "file_exists";
        public string Description => "Check if a file exists.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            bool exists = System.IO.File.Exists(path);
            return ToolResult.Success(exists ? "true" : "false");
        }
    }
}