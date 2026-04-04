using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.IO;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// Delete a directory, optionally with all contents
    /// </summary>
    public class DeleteDirectory : ITool
    {
        public string Name => "delete_directory";
        public string Description => "Delete a directory. Use with caution!";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path of the directory to delete", required: true)
            .Bool("recursive", "If true, delete all contents. If false, only delete if empty.", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            bool recursive = args.Value<bool?>("recursive") ?? false;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.Directory.Exists(path))
                return ToolResult.Error("Directory not found: " + path);

            try
            {
                System.IO.Directory.Delete(path, recursive);
                return ToolResult.Success("Deleted directory: " + path);
            }
            catch (IOException ex)
            {
                if (!recursive)
                    return ToolResult.Error("Directory is not empty. Use recursive=true to delete contents: " + ex.Message);
                
                return ToolResult.Error(ex.Message);
            }
        }
    }
}
