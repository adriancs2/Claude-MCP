using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// Copy multiple files in a single operation with flexible source/destination mapping
    /// </summary>
    public class BatchCopyFiles : ITool
    {
        public string Name => "batch_copy_files";
        public string Description => "Copy multiple files in a single operation. Supports copying to a single destination directory or mapping source->destination pairs.";

        public ToolParamList Params => new ToolParamList()
            .Array("files", "Array of file specifications. Each can be: (1) a source path string when using dest_dir, or (2) an object with 'source' and 'destination' properties for explicit mapping", required: true)
            .String("dest_dir", "Destination directory for all files (use when 'files' contains simple paths)")
            .Bool("overwrite", "If true, overwrite existing files.", defaultValue: false)
            .Bool("preserve_structure", "If true and using dest_dir, preserve relative directory structure from common parent.", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            JArray filesArray = args.Value<JArray>("files");
            string destDir = args.Value<string>("dest_dir");
            bool overwrite = args.Value<bool?>("overwrite") ?? false;
            bool preserveStructure = args.Value<bool?>("preserve_structure") ?? false;

            if (filesArray == null || filesArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "No files specified");

            int successCount = 0;
            int skipCount = 0;
            long totalBytes = 0;
            var errors = new List<string>();

            // Determine common parent for preserve_structure
            string commonParent = null;
            if (preserveStructure && !string.IsNullOrEmpty(destDir))
            {
                var allPaths = new List<string>();
                foreach (var item in filesArray)
                {
                    string p = item.Type == JTokenType.String
                        ? item.Value<string>()
                        : item.Value<string>("source");
                    if (!string.IsNullOrEmpty(p))
                        allPaths.Add(Path.GetDirectoryName(p));
                }
                commonParent = FindCommonParent(allPaths);
            }

            foreach (var item in filesArray)
            {
                string sourcePath;
                string destPath;

                if (item.Type == JTokenType.String)
                {
                    // Simple path string - requires dest_dir
                    sourcePath = item.Value<string>();
                    if (string.IsNullOrEmpty(destDir))
                    {
                        errors.Add(string.Format("{0}: dest_dir required for simple paths", Path.GetFileName(sourcePath)));
                        continue;
                    }

                    if (preserveStructure && commonParent != null)
                    {
                        string relativePath = GetRelativePath(commonParent, sourcePath);
                        destPath = Path.Combine(destDir, relativePath);
                    }
                    else
                    {
                        destPath = Path.Combine(destDir, Path.GetFileName(sourcePath));
                    }
                }
                else
                {
                    // Object with source/destination
                    sourcePath = item.Value<string>("source");
                    destPath = item.Value<string>("destination");

                    if (string.IsNullOrEmpty(sourcePath) || string.IsNullOrEmpty(destPath))
                    {
                        errors.Add("Invalid file entry: missing source or destination");
                        continue;
                    }
                }

                try
                {
                    if (!System.IO.File.Exists(sourcePath))
                    {
                        errors.Add(string.Format("{0}: source not found", Path.GetFileName(sourcePath)));
                        continue;
                    }

                    if (System.IO.File.Exists(destPath) && !overwrite)
                    {
                        skipCount++;
                        continue;
                    }

                    // Ensure destination directory exists
                    string destFileDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(destFileDir) && !System.IO.Directory.Exists(destFileDir))
                        System.IO.Directory.CreateDirectory(destFileDir);

                    System.IO.File.Copy(sourcePath, destPath, overwrite);
                    successCount++;
                    totalBytes += new FileInfo(sourcePath).Length;
                }
                catch (Exception ex)
                {
                    errors.Add(string.Format("{0}: {1}", Path.GetFileName(sourcePath), ex.Message));
                }
            }

            var results = new StringBuilder();
            results.AppendLine(string.Format("Batch copy complete: {0} copied, {1} skipped, {2} errors",
                successCount, skipCount, errors.Count));
            results.AppendLine(string.Format("Total size: {0}", FormatFileSize(totalBytes)));

            if (errors.Count > 0)
            {
                results.AppendLine();
                results.AppendLine("Errors:");
                foreach (var err in errors)
                {
                    results.AppendLine("  " + err);
                }
            }

            return errors.Count == 0
                ? ToolResult.Success(results.ToString())
                : ToolResult.Error("PARTIAL_SUCCESS", results.ToString());
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(basePath.Length);

            return fullPath;
        }

        private static string FindCommonParent(List<string> paths)
        {
            if (paths == null || paths.Count == 0)
                return null;

            if (paths.Count == 1)
                return paths[0];

            string common = paths[0];
            foreach (string path in paths)
            {
                while (!string.IsNullOrEmpty(common) &&
                       !path.StartsWith(common, StringComparison.OrdinalIgnoreCase))
                {
                    common = Path.GetDirectoryName(common);
                }
            }

            return common;
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
