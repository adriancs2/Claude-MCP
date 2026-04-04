using System;
using System.IO.Compression;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Zip
{
    public class ZipFolderTool : ITool
    {
        public string Name => "zip_folder";
        public string Description => "Create a new ZIP file from an entire folder";

        public ToolParamList Params => new ToolParamList()
            .String("zip_path", "Full path to the ZIP file to create or update", required: true)
            .String("folder_path", "Full path to the folder to zip", required: true)
            .Bool("recursive", "Include subdirectories (default: true)", defaultValue: true)
            .Bool("overwrite", "If true, overwrite existing ZIP file. If false, add to existing ZIP (default: false)", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string zipPath = args.Value<string>("zip_path");
            if (string.IsNullOrEmpty(zipPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'zip_path' parameter");

            string folderPath = args.Value<string>("folder_path");
            if (string.IsNullOrEmpty(folderPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'folder_path' parameter");

            bool recursive = args.Value<bool?>("recursive") ?? true;
            bool overwrite = args.Value<bool?>("overwrite") ?? false;

            if (!System.IO.Directory.Exists(folderPath))
                return ToolResult.Error($"Folder not found: {folderPath}");

            var searchOption = recursive ? System.IO.SearchOption.AllDirectories : System.IO.SearchOption.TopDirectoryOnly;
            var files = System.IO.Directory.GetFiles(folderPath, "*", searchOption);

            if (files.Length == 0)
                return ToolResult.Error($"No files found in folder: {folderPath}");

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
                string basePath = System.IO.Path.GetFullPath(folderPath).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                foreach (var file in files)
                {
                    string fullFile = System.IO.Path.GetFullPath(file);
                    string entryName = fullFile.StartsWith(basePath, StringComparison.OrdinalIgnoreCase)
                        ? fullFile.Substring(basePath.Length)
                        : fullFile;

                    if (mode == ZipArchiveMode.Update)
                    {
                        var existingEntry = zip.GetEntry(entryName);
                        if (existingEntry != null)
                            existingEntry.Delete();
                    }
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            return ToolResult.Success(string.Format("ZIP {0}: {1}\nFiles added: {2}\nRecursive: {3}",
                zipExists ? "updated" : "created", zipPath, files.Length, recursive));
        }
    }
}
