using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Zip
{
    public class ExtractZipContent : ITool
    {
        public string Name => "extract_zip_content";
        public string Description => "Extract specific files or folders from a ZIP file based on patterns (supports wildcards like *.dll, lib/**/*).";

        public ToolParamList Params => new ToolParamList()
            .String("zip_path", "Full path to the ZIP file", required: true)
            .String("destination_path", "Full path to the destination folder", required: true)
            .Array("patterns", "Array of file patterns to extract (e.g., [\"*.dll\", \"lib/net48/**\"])", required: true)
            .Bool("overwrite", "Overwrite existing files (default: true)", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string zipPath = args.Value<string>("zip_path");
            if (string.IsNullOrEmpty(zipPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'zip_path' parameter");

            string destinationPath = args.Value<string>("destination_path");
            if (string.IsNullOrEmpty(destinationPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination_path' parameter");

            var patternsArray = args["patterns"] as JArray;
            if (patternsArray == null || patternsArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "patterns array is required and cannot be empty");

            bool overwrite = args.Value<bool?>("overwrite") ?? true;

            if (!System.IO.File.Exists(zipPath))
                return ToolResult.Error($"ZIP file not found: {zipPath}");

            var patterns = patternsArray.Select(p => p.ToString()).ToList();
            System.IO.Directory.CreateDirectory(destinationPath);
            int filesExtracted = 0;
            var extractedFiles = new List<string>();

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;

                    bool matches = patterns.Any(p => MatchesPattern(entry.FullName, p));
                    if (!matches) continue;

                    string destinationFilePath = System.IO.Path.Combine(destinationPath, entry.FullName);
                    string directoryPath = System.IO.Path.GetDirectoryName(destinationFilePath);
                    if (!string.IsNullOrEmpty(directoryPath) && !System.IO.Directory.Exists(directoryPath))
                        System.IO.Directory.CreateDirectory(directoryPath);

                    if (overwrite || !System.IO.File.Exists(destinationFilePath))
                    {
                        entry.ExtractToFile(destinationFilePath, overwrite);
                        filesExtracted++;
                        extractedFiles.Add(entry.FullName);
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format("Extracted to: {0}", destinationPath));
            sb.AppendLine(string.Format("Files extracted: {0}", filesExtracted));
            sb.AppendLine(string.Format("Patterns: {0}", string.Join(", ", patterns)));
            if (extractedFiles.Count <= 20)
            {
                sb.AppendLine("Files:");
                foreach (var f in extractedFiles)
                    sb.AppendLine(string.Format("  {0}", f));
            }

            return ToolResult.Success(sb.ToString());
        }

        private static bool MatchesPattern(string path, string pattern)
        {
            path = path.Replace('\\', '/');
            pattern = pattern.Replace('\\', '/');

            if (pattern.Contains("**"))
            {
                var parts = pattern.Split(new[] { "**" }, StringSplitOptions.None);
                if (parts.Length == 2)
                {
                    string prefix = parts[0].TrimEnd('/');
                    string suffix = parts[1].TrimStart('/');
                    bool prefixMatch = string.IsNullOrEmpty(prefix) ||
                        path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
                    bool suffixMatch = string.IsNullOrEmpty(suffix) ||
                        MatchesSimplePattern(path, suffix);
                    return prefixMatch && suffixMatch;
                }
            }

            return MatchesSimplePattern(path, pattern);
        }

        private static bool MatchesSimplePattern(string path, string pattern)
        {
            if (pattern.Contains("*"))
            {
                string regexPattern = "^" + Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                return Regex.IsMatch(path, regexPattern, RegexOptions.IgnoreCase);
            }
            return path.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }
    }
}
