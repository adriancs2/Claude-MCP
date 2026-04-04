using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace MCP2.Tools.Search
{
    /// <summary>
    /// Search for a pattern across all files in a directory
    /// </summary>
    public class FindInFiles : ITool
    {
        public string Name => "find_in_files";
        public string Description => "Search for a pattern across all files in a directory. Supports presets (dotnet, web, python) to auto-exclude bin/obj/packages/etc. Use context_lines to see surrounding code.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Directory path to search in", required: true)
            .String("pattern", "Text pattern to search for", required: true)
            .String("file_pattern", "File filter pattern (e.g., \"*.aspx\", \"*.cs\", \"*.aspx;*.cs\")")
            .Bool("recursive", "Search subdirectories", defaultValue: true)
            .Bool("case_sensitive", "Whether search is case-sensitive", defaultValue: true)
            .Int("context_lines", "Number of context lines before and after each match (0 = no context, default)", defaultValue: 0)
            .StringEnum("preset", "Use preset exclusions: dotnet, web, python", new[] { "dotnet", "web", "python" })
            .String("exclude_folders", "Semicolon-separated folder names to skip (e.g., \"bin;obj;packages\")")
            .String("exclude_extensions", "Semicolon-separated extensions to skip (e.g., \".dll;.exe;.png\")");

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string pattern = args.Value<string>("pattern");
            string filePattern = args.Value<string>("file_pattern") ?? "*";
            bool recursive = args.Value<bool?>("recursive") ?? true;
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
            int contextLines = args.Value<int?>("context_lines") ?? 0;
            string preset = args.Value<string>("preset");
            string excludeFolders = args.Value<string>("exclude_folders");
            string excludeExtensions = args.Value<string>("exclude_extensions");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(pattern))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'pattern' parameter");

            

            if (!System.IO.Directory.Exists(path))
                return ToolResult.Error(string.Format("Directory not found: {0}", path));

            // Apply presets
            HashSet<string> excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(preset))
            {
                switch (preset.ToLower())
                {
                    case "dotnet":
                        excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { "bin", "obj", "packages", ".vs", ".git" };
                        excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { ".dll", ".exe", ".pdb", ".cache", ".png", ".jpg", ".zip", ".nupkg" };
                        break;
                    case "web":
                        excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { "node_modules", "dist", "build", ".git" };
                        excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { ".png", ".jpg", ".woff", ".map", ".min.js", ".min.css" };
                        break;
                    case "python":
                        excludedFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { "__pycache__", "venv", ".git", ".tox" };
                        excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                            { ".pyc", ".pyo", ".pyd", ".so", ".egg", ".png" };
                        break;
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(excludeFolders))
                {
                    foreach (string f in excludeFolders.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        excludedFolders.Add(f.Trim());
                }
                if (!string.IsNullOrEmpty(excludeExtensions))
                {
                    foreach (string e in excludeExtensions.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                        excludedExtensions.Add(e.Trim().StartsWith(".") ? e.Trim() : "." + e.Trim());
                }
            }

            StringComparison comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            StringBuilder sb = new StringBuilder();
            int totalMatches = 0;
            int filesWithMatches = 0;

            // Support multiple file patterns
            string[] filePatterns = filePattern.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            var allFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string fp in filePatterns)
            {
                try
                {
                    foreach (string f in System.IO.Directory.GetFiles(path, fp.Trim(), searchOption))
                        allFiles.Add(f);
                }
                catch (UnauthorizedAccessException) { }
            }

            foreach (string filePath in allFiles)
            {
                // Check folder exclusions
                bool skip = false;
                string relativePath = filePath.Substring(path.Length).TrimStart(Path.DirectorySeparatorChar);
                string[] parts = relativePath.Split(Path.DirectorySeparatorChar);
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    if (excludedFolders.Contains(parts[i]))
                    {
                        skip = true;
                        break;
                    }
                }
                if (skip) continue;

                // Check extension exclusions
                string ext = Path.GetExtension(filePath);
                if (excludedExtensions.Contains(ext)) continue;

                // Skip binary files
                if (FileOperations.IsBinaryFile(filePath)) continue;

                try
                {
                    string[] lines = System.IO.File.ReadAllLines(filePath, Encoding.UTF8);
                    bool fileHasMatch = false;

                    for (int i = 0; i < lines.Length; i++)
                    {
                        if (lines[i].IndexOf(pattern, comparison) >= 0)
                        {
                            if (!fileHasMatch)
                            {
                                sb.AppendLine(string.Format("--- {0} ---", relativePath));
                                fileHasMatch = true;
                                filesWithMatches++;
                            }

                            if (contextLines > 0)
                            {
                                // Show context lines before
                                int startCtx = Math.Max(0, i - contextLines);
                                for (int c = startCtx; c < i; c++)
                                {
                                    sb.AppendLine(string.Format("  {0}  {1}", c + 1, lines[c].TrimEnd()));
                                }

                                // Show the matching line with marker
                                sb.AppendLine(string.Format("  {0}> {1}", i + 1, lines[i].TrimEnd()));

                                // Show context lines after
                                int endCtx = Math.Min(lines.Length - 1, i + contextLines);
                                for (int c = i + 1; c <= endCtx; c++)
                                {
                                    sb.AppendLine(string.Format("  {0}  {1}", c + 1, lines[c].TrimEnd()));
                                }

                                sb.AppendLine();
                            }
                            else
                            {
                                // Original compact format
                                sb.AppendLine(string.Format("  {0}: {1}", i + 1, lines[i].TrimEnd()));
                            }

                            totalMatches++;
                        }
                    }

                    if (fileHasMatch && contextLines == 0)
                        sb.AppendLine();
                }
                catch { /* skip unreadable files */ }
            }

            if (totalMatches == 0)
                return ToolResult.Success(string.Format("No matches found for \"{0}\" in {1}", pattern, path));

            sb.Insert(0, string.Format("Found {0} match(es) in {1} file(s):\n\n", totalMatches, filesWithMatches));
            return ToolResult.Success(sb.ToString());
        }
    }
}