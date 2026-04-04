using System.IO.Compression;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Zip
{
    public class ExtractZip : ITool
    {
        public string Name => "extract_zip";
        public string Description => "Extract all contents of a ZIP file to a destination folder";

        public ToolParamList Params => new ToolParamList()
            .String("zip_path", "Full path to the ZIP file to extract", required: true)
            .String("destination_path", "Full path to the destination folder", required: true)
            .Bool("overwrite", "Overwrite existing files (default: true)", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string zipPath = args.Value<string>("zip_path");
            if (string.IsNullOrEmpty(zipPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'zip_path' parameter");

            string destinationPath = args.Value<string>("destination_path");
            if (string.IsNullOrEmpty(destinationPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination_path' parameter");

            bool overwrite = args.Value<bool?>("overwrite") ?? true;

            if (!System.IO.File.Exists(zipPath))
                return ToolResult.Error($"ZIP file not found: {zipPath}");

            System.IO.Directory.CreateDirectory(destinationPath);
            int filesExtracted = 0;

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    string destinationFilePath = System.IO.Path.Combine(destinationPath, entry.FullName);
                    string directoryPath = System.IO.Path.GetDirectoryName(destinationFilePath);
                    if (!string.IsNullOrEmpty(directoryPath) && !System.IO.Directory.Exists(directoryPath))
                        System.IO.Directory.CreateDirectory(directoryPath);

                    if (overwrite || !System.IO.File.Exists(destinationFilePath))
                    {
                        entry.ExtractToFile(destinationFilePath, overwrite);
                        filesExtracted++;
                    }
                }
            }

            return ToolResult.Success(string.Format("Extracted to: {0}\nFiles extracted: {1}",
                destinationPath, filesExtracted));
        }
    }
}
