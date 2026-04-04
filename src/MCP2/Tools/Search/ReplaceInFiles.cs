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
    /// Replace text across all matching files in a directory
    /// </summary>
    public class ReplaceInFiles : ITool
    {
        public string Name => "replace_in_files";
        public string Description => "Replace text across all matching files in a directory. Supports presets (dotnet, web, python) to auto-exclude bin/obj/packages/etc. Perfect for renaming CSS classes, updating namespaces, or project-wide refactoring.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Directory path to search in", required: true)
            .String("find_text", "Text to find", required: true)
            .String("replace_text", "Text to replace with", required: true)
            .String("file_pattern", "File filter pattern (e.g., \"*.aspx\", \"*.cs\")")
            .Bool("recursive", "Search subdirectories", defaultValue: true)
            .Bool("case_sensitive", "Whether search is case-sensitive", defaultValue: true)
            .Bool("create_backup", "Create backup before editing", defaultValue: true)
            .StringEnum("preset", "Use preset exclusions: dotnet, web, python", new[] { "dotnet", "web", "python" })
            .String("exclude_folders", "Semicolon-separated folder names to skip")
            .String("exclude_extensions", "Semicolon-separated extensions to skip");

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string findText = args.Value<string>("find_text");
            string replaceText = args.Value<string>("replace_text");
            string filePattern = args.Value<string>("file_pattern") ?? "*";
            bool recursive = args.Value<bool?>("recursive") ?? true;
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? true;
            bool createBackup = args.Value<bool?>("create_backup") ?? true;
            string preset = args.Value<string>("preset");
            string excludeFolders = args.Value<string>("exclude_folders");
            string excludeExtensions = args.Value<string>("exclude_extensions");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(findText))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'find_text' parameter");
            if (replaceText == null)
                return ToolResult.Error("INVALID_PARAMS", "Missing 'replace_text' parameter");

            
            // Normalize line endings for consistent matching
            findText = FileOperations.NormalizeLineEndings(findText);
            replaceText = FileOperations.NormalizeLineEndings(replaceText);


            if (!System.IO.Directory.Exists(path))
                return ToolResult.Error(string.Format("Directory not found: {0}", path));

            // Apply presets (same logic as FindInFiles)
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
            var backupService = createBackup ? new BackupService() : null;
            int filesModified = 0;
            int totalReplacements = 0;
            var results = new StringBuilder();

            // Collect files
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
                    Encoding encoding = Encoding.UTF8;
                    string content = FileOperations.NormalizeLineEndings(System.IO.File.ReadAllText(filePath, encoding));

                    // Count occurrences
                    int count = 0;
                    int idx = 0;
                    while ((idx = content.IndexOf(findText, idx, comparison)) >= 0)
                    {
                        count++;
                        idx += findText.Length;
                    }

                    if (count > 0)
                    {
                        if (backupService != null)
                            backupService.CreateBackup(filePath);

                        // Perform replacement
                        string newContent;
                        if (caseSensitive)
                        {
                            newContent = content.Replace(findText, replaceText);
                        }
                        else
                        {
                            // Case-insensitive replace
                            StringBuilder sbReplace = new StringBuilder();
                            int lastIdx = 0;
                            idx = 0;
                            while ((idx = content.IndexOf(findText, idx, comparison)) >= 0)
                            {
                                sbReplace.Append(content.Substring(lastIdx, idx - lastIdx));
                                sbReplace.Append(replaceText);
                                idx += findText.Length;
                                lastIdx = idx;
                            }
                            sbReplace.Append(content.Substring(lastIdx));
                            newContent = sbReplace.ToString();
                        }

                        System.IO.File.WriteAllText(filePath, newContent, encoding);
                        filesModified++;
                        totalReplacements += count;
                        results.AppendLine(string.Format("  {0}: {1} replacement(s)", relativePath, count));
                    }
                }
                catch { /* skip unreadable files */ }
            }

            if (totalReplacements == 0)
                return ToolResult.Success(string.Format("No occurrences of \"{0}\" found in {1}", findText, path));

            results.Insert(0, string.Format("Replaced {0} occurrence(s) in {1} file(s):\n", totalReplacements, filesModified));
            return ToolResult.Success(results.ToString());
        }
    }
}