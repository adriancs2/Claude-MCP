using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class MoveFile : ITool
    {
        public string Name => "move_file";
        public string Description => "Move a file to a new location.";
        
        public ToolParamList Params => new ToolParamList()
            .String("source_path", "Source file path", required: true)
            .String("destination_path", "Destination file path", required: true)
            .Bool("overwrite", "Overwrite destination if it exists", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string sourcePath = args.Value<string>("source_path");
            string destPath = args.Value<string>("destination_path");
            bool overwrite = args.Value<bool?>("overwrite") ?? false;

            if (string.IsNullOrEmpty(sourcePath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'source_path' parameter");
            if (string.IsNullOrEmpty(destPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination_path' parameter");

            
            

            if (!System.IO.File.Exists(sourcePath))
                return ToolResult.Error($"Source file not found: {sourcePath}");

            if (System.IO.File.Exists(destPath) && !overwrite)
                return ToolResult.Error($"Destination file already exists: {destPath}\nUse overwrite=true to replace it.");

            // Ensure destination directory exists
            string destDir = System.IO.Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir) && !System.IO.Directory.Exists(destDir))
                System.IO.Directory.CreateDirectory(destDir);

            if (overwrite && System.IO.File.Exists(destPath))
                System.IO.File.Delete(destPath);

            System.IO.File.Move(sourcePath, destPath);
            
            return ToolResult.Success($"File moved successfully:\nFrom: {sourcePath}\nTo: {destPath}");
        }
    }
}