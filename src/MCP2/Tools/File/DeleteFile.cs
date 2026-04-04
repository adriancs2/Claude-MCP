using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class DeleteFile : ITool
    {
        public string Name => "delete_file";
        public string Description => "Permanently delete a file.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            System.IO.File.Delete(path);
            
            return ToolResult.Success($"File deleted successfully: {path}");
        }
    }
}