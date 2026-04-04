using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// Create a new directory, including parent directories if needed
    /// </summary>
    public class CreateDirectory : ITool
    {
        public string Name => "create_directory";
        public string Description => "Create a new directory. Parent directories will be created if they don't exist.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path of the directory to create", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (System.IO.Directory.Exists(path))
                return ToolResult.Success("Directory already exists: " + path);

            System.IO.Directory.CreateDirectory(path);
            return ToolResult.Success("Created directory: " + path);
        }
    }
}
