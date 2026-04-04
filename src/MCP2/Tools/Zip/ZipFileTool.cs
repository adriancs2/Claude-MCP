using System.IO.Compression;
using System.Linq;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Zip
{
    public class ZipFileTool : ITool
    {
        public string Name => "zip_file";
        public string Description => "Create a new ZIP file or add files to an existing ZIP archive";

        public ToolParamList Params => new ToolParamList()
            .String("zip_path", "Full path to the ZIP file to create or update", required: true)
            .Array("files", "Array of file paths to add to the ZIP", required: true)
            .Bool("overwrite", "If true, overwrite existing ZIP file. If false, add to existing ZIP (default: false)", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string zipPath = args.Value<string>("zip_path");
            if (string.IsNullOrEmpty(zipPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'zip_path' parameter");

            var filesArray = args["files"] as JArray;
            if (filesArray == null || filesArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "files array is required and cannot be empty");

            bool overwrite = args.Value<bool?>("overwrite") ?? false;

            var files = filesArray.Select(f => f.ToString()).ToList();

            var missingFiles = files.Where(f => !System.IO.File.Exists(f)).ToList();
            if (missingFiles.Any())
                return ToolResult.Error("FILE_NOT_FOUND",
                    string.Format("Files not found: {0}", string.Join(", ", missingFiles)));

            bool zipExists = System.IO.File.Exists(zipPath);
            if (zipExists && overwrite)
            {
                System.IO.File.Delete(zipPath);
                zipExists = false;
            }

            string parentDir = System.IO.Path.GetDirectoryName(zipPath);
            if (!string.IsNullOrEmpty(parentDir) && !System.IO.Directory.Exists(parentDir))
                System.IO.Directory.CreateDirectory(parentDir);

            ZipArchiveMode mode = zipExists ? ZipArchiveMode.Update : ZipArchiveMode.Create;

            using (ZipArchive zip = ZipFile.Open(zipPath, mode))
            {
                foreach (var file in files)
                {
                    string entryName = System.IO.Path.GetFileName(file);
                    if (mode == ZipArchiveMode.Update)
                    {
                        var existingEntry = zip.GetEntry(entryName);
                        if (existingEntry != null)
                            existingEntry.Delete();
                    }
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            return ToolResult.Success(string.Format("ZIP {0}: {1}\nFiles added: {2}",
                zipExists ? "updated" : "created", zipPath, files.Count));
        }
    }
}
