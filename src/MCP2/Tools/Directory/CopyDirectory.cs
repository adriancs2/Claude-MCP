using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.IO;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// Copy an entire directory including all files and subdirectories
    /// </summary>
    public class CopyDirectory : ITool
    {
        public string Name => "copy_directory";
        public string Description => "Copy an entire directory including all files and subdirectories to a new location.";

        public ToolParamList Params => new ToolParamList()
            .String("source", "Full path of the source directory", required: true)
            .String("destination", "Full path of the destination directory", required: true)
            .Bool("overwrite", "If true, overwrite existing files in destination.", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string source = args.Value<string>("source");
            string destination = args.Value<string>("destination");
            bool overwrite = args.Value<bool?>("overwrite") ?? false;

            if (string.IsNullOrEmpty(source))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'source' parameter");
            if (string.IsNullOrEmpty(destination))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination' parameter");

            if (!System.IO.Directory.Exists(source))
                return ToolResult.Error("Source directory not found: " + source);

            int filesCopied = 0;
            int dirsCopied = 0;
            long totalBytes = 0;

            // Create destination directory
            if (!System.IO.Directory.Exists(destination))
            {
                System.IO.Directory.CreateDirectory(destination);
                dirsCopied++;
            }

            // Copy all subdirectories
            foreach (string dirPath in System.IO.Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                string newDirPath = dirPath.Replace(source, destination);
                if (!System.IO.Directory.Exists(newDirPath))
                {
                    System.IO.Directory.CreateDirectory(newDirPath);
                    dirsCopied++;
                }
            }

            // Copy all files
            foreach (string filePath in System.IO.Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string newFilePath = filePath.Replace(source, destination);

                if (System.IO.File.Exists(newFilePath) && !overwrite)
                    continue;

                System.IO.File.Copy(filePath, newFilePath, overwrite);
                filesCopied++;
                totalBytes += new FileInfo(filePath).Length;
            }

            return ToolResult.Success(string.Format(
                "Copied directory: {0} -> {1}\nFiles: {2}, Directories: {3}, Total size: {4}",
                source, destination, filesCopied, dirsCopied, FormatFileSize(totalBytes)));
        }

        private static string FormatFileSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int suffixIndex = 0;
            double size = bytes;

            while (size >= 1024 && suffixIndex < suffixes.Length - 1)
            {
                size /= 1024;
                suffixIndex++;
            }

            return string.Format("{0:N1} {1}", size, suffixes[suffixIndex]);
        }
    }
}
