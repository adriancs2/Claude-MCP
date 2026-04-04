using System;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Zip
{
    public class ListZipContents : ITool
    {
        public string Name => "list_zip_contents";
        public string Description => "List all files and folders inside a ZIP file with their sizes";

        public ToolParamList Params => new ToolParamList()
            .String("zip_path", "Full path to the ZIP file", required: true)
            .String("pattern", "Optional filter pattern (e.g., \"*.dll\", \"lib/**\")");

        public ToolResult Execute(JObject args)
        {
            string zipPath = args.Value<string>("zip_path");
            if (string.IsNullOrEmpty(zipPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'zip_path' parameter");

            string pattern = args.Value<string>("pattern");

            if (!System.IO.File.Exists(zipPath))
                return ToolResult.Error($"ZIP file not found: {zipPath}");

            var sb = new StringBuilder();
            long totalSize = 0;
            int fileCount = 0;

            using (ZipArchive zip = ZipFile.OpenRead(zipPath))
            {
                sb.AppendLine(string.Format("Contents of: {0}", zipPath));
                sb.AppendLine(string.Format("Total entries: {0}", zip.Entries.Count));
                sb.AppendLine(new string('-', 60));

                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (!string.IsNullOrEmpty(pattern) && !MatchesPattern(entry.FullName, pattern))
                        continue;

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        sb.AppendLine(string.Format("[DIR]  {0}", entry.FullName));
                    }
                    else
                    {
                        sb.AppendLine(string.Format("[FILE] {0} ({1})", entry.FullName, FormatFileSize(entry.Length)));
                        totalSize += entry.Length;
                        fileCount++;
                    }
                }

                sb.AppendLine(new string('-', 60));
                sb.AppendLine(string.Format("Files: {0}, Total uncompressed size: {1}", fileCount, FormatFileSize(totalSize)));
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

        private static string FormatFileSize(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB" };
            int order = 0;
            double size = bytes;
            while (size >= 1024 && order < sizes.Length - 1)
            {
                order++;
                size /= 1024;
            }
            return string.Format("{0:0.##} {1}", size, sizes[order]);
        }
    }
}
