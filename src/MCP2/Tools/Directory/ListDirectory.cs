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
    /// List files and folders in a directory
    /// </summary>
    public class ListDirectory : ITool
    {
        public string Name => "list_directory";
        public string Description => "List files and folders in a directory.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the directory", required: true)
            .Bool("recursive", "Include subdirectories", defaultValue: false)
            .String("pattern", "Filter pattern (e.g., \"*.cs\") or multiple patterns separated by semicolons (e.g., \"*.cs;*.aspx\")");

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            bool recursive = args.Value<bool?>("recursive") ?? false;
            string pattern = args.Value<string>("pattern") ?? "*";

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.Directory.Exists(path))
                return ToolResult.Error(string.Format("Directory not found: {0}", path));

            StringBuilder sb = new StringBuilder();
            SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // List directories first
            try
            {
                string[] dirs = System.IO.Directory.GetDirectories(path, "*", searchOption);
                foreach (string dir in dirs)
                {
                    string relativePath = GetRelativePath(path, dir);
                    sb.AppendLine(string.Format("[DIR]  {0}", relativePath));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip directories we can't access
            }

            // List files - support multiple patterns separated by semicolons
            try
            {
                string[] patterns = pattern.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string p in patterns)
                {
                    string trimmedPattern = p.Trim();
                    if (!string.IsNullOrEmpty(trimmedPattern))
                    {
                        string[] files = System.IO.Directory.GetFiles(path, trimmedPattern, searchOption);
                        foreach (string file in files)
                        {
                            matchedFiles.Add(file);
                        }
                    }
                }

                // Sort and output
                var sortedFiles = new List<string>(matchedFiles);
                sortedFiles.Sort(StringComparer.OrdinalIgnoreCase);

                foreach (string file in sortedFiles)
                {
                    string relativePath = GetRelativePath(path, file);
                    sb.AppendLine(string.Format("[FILE] {0}", relativePath));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Skip files we can't access
            }

            if (sb.Length == 0)
                return ToolResult.Success("Directory is empty or no matches found.");

            return ToolResult.Success(sb.ToString());
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(basePath.Length);

            return fullPath;
        }
    }
}
