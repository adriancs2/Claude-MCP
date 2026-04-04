using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.IO;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// Move or rename an entire directory including all contents
    /// </summary>
    public class MoveDirectory : ITool
    {
        public string Name => "move_directory";
        public string Description => "Move or rename an entire directory including all contents.";

        public ToolParamList Params => new ToolParamList()
            .String("source", "Full path of the source directory", required: true)
            .String("destination", "Full path of the destination directory", required: true);

        public ToolResult Execute(JObject args)
        {
            string source = args.Value<string>("source");
            string destination = args.Value<string>("destination");

            if (string.IsNullOrEmpty(source))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'source' parameter");
            if (string.IsNullOrEmpty(destination))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination' parameter");

            
            

            if (!System.IO.Directory.Exists(source))
                return ToolResult.Error("Source directory not found: " + source);

            if (System.IO.Directory.Exists(destination))
                return ToolResult.Error("Destination directory already exists: " + destination);

            // Ensure parent directory exists
            string parentDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(parentDir) && !System.IO.Directory.Exists(parentDir))
                System.IO.Directory.CreateDirectory(parentDir);

            System.IO.Directory.Move(source, destination);
            return ToolResult.Success(string.Format("Moved directory: {0} -> {1}", source, destination));
        }
    }
}
