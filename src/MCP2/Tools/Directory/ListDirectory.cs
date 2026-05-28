using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// List files and folders in a directory.
    /// Supports glob-style patterns including ** for recursive matching across path segments.
    /// </summary>
    public class ListDirectory : ITool
    {
        public string Name => "list_directory";
        public string Description => "List files and folders in a directory. Supports glob patterns (*, ?, **) and multiple patterns separated by semicolons. Patterns may include path segments like 'src/**/*.cs'. Use exclude to skip patterns like 'node_modules;bin;obj'.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the base directory", required: true)
            .Bool("recursive", "Include subdirectories (auto-enabled if pattern contains **)", defaultValue: false)
            .String("pattern", "Glob pattern(s). Examples: '*.cs', 'src/**/test_*.py', '*.cs;*.aspx'. Use ** for recursive sub-path matching.")
            .String("exclude", "Exclude pattern(s), semicolon-separated. Matches directory names or file globs. Example: 'node_modules;bin;obj;.git'")
            .String("sort", "Sort order: 'name' (default) or 'modified' (newest first)")
            .Int("limit", "Max number of files to return (default: no limit)");

        // Sensible defaults so a recursive walk of a normal project doesn't drown in build/vendor noise.
        // Only applied when the caller passes no exclude param.
        private static readonly string[] DefaultExcludes = new[]
        {
            "node_modules", ".git", "bin", "obj", ".vs", ".idea", "dist", "build", "__pycache__", ".next"
        };

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            bool recursive = args.Value<bool?>("recursive") ?? false;
            string pattern = args.Value<string>("pattern") ?? "*";
            string excludeParam = args.Value<string>("exclude");
            string sort = (args.Value<string>("sort") ?? "name").ToLowerInvariant();
            int? limit = args.Value<int?>("limit");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            if (!System.IO.Directory.Exists(path))
                return ToolResult.Error(string.Format("Directory not found: {0}", path));

            // Auto-enable recursive walking if any pattern contains **.
            // Saves the caller from remembering to set the flag.
            string[] rawPatterns = pattern.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            bool anyDoubleStar = rawPatterns.Any(p => p.Contains("**"));
            if (anyDoubleStar) recursive = true;

            // Build exclusion set. Caller's list completely overrides defaults — if you want
            // defaults plus extras, pass them explicitly.
            HashSet<string> excludeDirs;
            List<Regex> excludeFileRegexes;
            ParseExcludes(excludeParam, out excludeDirs, out excludeFileRegexes);

            StringBuilder sb = new StringBuilder();
            SearchOption searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            // Compile each user pattern into (filename-glob, optional path-prefix-regex).
            // A pattern like "src/**/test_*.py" splits into:
            //   - filename glob: "test_*.py"
            //   - path regex:    must contain "src" segment somewhere before the file
            var compiledPatterns = rawPatterns.Select(CompilePattern).ToList();

            // Enumerate files. Use EnumerateFiles for lazy iteration so we can short-circuit on limit.
            var matchedFiles = new List<FileInfo>();
            try
            {
                foreach (string file in EnumerateFilesSafe(path, searchOption, excludeDirs))
                {
                    string relativePath = GetRelativePath(path, file);
                    string fileName = Path.GetFileName(file);

                    // File-level exclusions (e.g. "*.tmp" in exclude)
                    if (excludeFileRegexes.Any(rx => rx.IsMatch(fileName)))
                        continue;

                    // Match against any compiled pattern (OR logic across semicolon list)
                    bool matches = compiledPatterns.Any(cp => cp.Matches(relativePath, fileName));
                    if (!matches) continue;

                    matchedFiles.Add(new FileInfo(file));
                }
            }
            catch (UnauthorizedAccessException)
            {
                // Partial results are better than no results.
            }

            // Sort
            if (sort == "modified")
                matchedFiles.Sort((a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
            else
                matchedFiles.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.FullName, b.FullName));

            // Apply limit
            int truncated = 0;
            if (limit.HasValue && matchedFiles.Count > limit.Value)
            {
                truncated = matchedFiles.Count - limit.Value;
                matchedFiles = matchedFiles.Take(limit.Value).ToList();
            }

            // Directories (only when no file pattern is active — listing dirs alongside a
            // "*.cs" query is just noise. Show dirs when caller used the default "*" pattern.)
            bool showDirs = pattern == "*" || pattern == "*.*";
            if (showDirs)
            {
                try
                {
                    foreach (string dir in EnumerateDirectoriesSafe(path, searchOption, excludeDirs))
                    {
                        string relativePath = GetRelativePath(path, dir);
                        sb.AppendLine(string.Format("[DIR]  {0}", relativePath));
                    }
                }
                catch (UnauthorizedAccessException) { }
            }

            foreach (var fi in matchedFiles)
            {
                string relativePath = GetRelativePath(path, fi.FullName);
                if (sort == "modified")
                    sb.AppendLine(string.Format("[FILE] {0}\t{1:yyyy-MM-dd HH:mm}", relativePath, fi.LastWriteTime));
                else
                    sb.AppendLine(string.Format("[FILE] {0}", relativePath));
            }

            if (truncated > 0)
                sb.AppendLine(string.Format("... and {0} more (truncated by limit={1})", truncated, limit.Value));

            if (sb.Length == 0)
                return ToolResult.Success("Directory is empty or no matches found.");

            return ToolResult.Success(sb.ToString());
        }

        // -----------------------------------------------------------------
        // Pattern compilation
        // -----------------------------------------------------------------

        private class CompiledPattern
        {
            public string FileGlob;            // e.g. "*.cs"
            public Regex FileNameRegex;        // compiled from FileGlob
            public Regex PathPrefixRegex;      // null if no path component in pattern
            public bool HasDoubleStar;

            public bool Matches(string relativePath, string fileName)
            {
                if (!FileNameRegex.IsMatch(fileName)) return false;
                if (PathPrefixRegex == null) return true;
                // Match against the relative path's directory portion (forward-slash normalized)
                string normalized = relativePath.Replace('\\', '/');
                return PathPrefixRegex.IsMatch(normalized);
            }
        }

        private static CompiledPattern CompilePattern(string raw)
        {
            string p = raw.Trim().Replace('\\', '/');
            var cp = new CompiledPattern { HasDoubleStar = p.Contains("**") };

            int lastSlash = p.LastIndexOf('/');
            string pathPart, filePart;
            if (lastSlash >= 0)
            {
                pathPart = p.Substring(0, lastSlash);
                filePart = p.Substring(lastSlash + 1);
            }
            else
            {
                pathPart = null;
                filePart = p;
            }

            // If filePart is "**" alone (e.g. "src/**"), treat as match-all-files
            if (filePart == "**") filePart = "*";

            cp.FileGlob = filePart;
            cp.FileNameRegex = new Regex("^" + GlobToRegex(filePart) + "$", RegexOptions.IgnoreCase);

            if (pathPart != null)
            {
                // Build a regex that matches anywhere in the relative path's directory chain.
                // "src/**" -> path must start with src/, anything after
                // "src/**/test" -> src/<anything>/test/
                // "**/foo" -> any path containing /foo/
                string pathRegex = GlobToRegex(pathPart);
                // The full relative path looks like "subdir1/subdir2/file.cs". We want the
                // directory prefix to match. Anchor to start, allow trailing slash + filename.
                cp.PathPrefixRegex = new Regex("^" + pathRegex + "/", RegexOptions.IgnoreCase);
            }

            return cp;
        }

        private static string GlobToRegex(string glob)
        {
            var sb = new StringBuilder();
            int i = 0;
            while (i < glob.Length)
            {
                char c = glob[i];
                if (c == '*')
                {
                    if (i + 1 < glob.Length && glob[i + 1] == '*')
                    {
                        // ** matches across path separators (zero or more segments)
                        sb.Append(".*");
                        i += 2;
                        // Swallow a trailing / so "**/foo" works cleanly
                        if (i < glob.Length && glob[i] == '/') i++;
                    }
                    else
                    {
                        // * matches anything except / within one segment
                        sb.Append("[^/]*");
                        i++;
                    }
                }
                else if (c == '?')
                {
                    sb.Append("[^/]");
                    i++;
                }
                else if (c == '/')
                {
                    sb.Append('/');
                    i++;
                }
                else if ("\\.+()|^$[]{}".IndexOf(c) >= 0)
                {
                    sb.Append('\\').Append(c);
                    i++;
                }
                else
                {
                    sb.Append(c);
                    i++;
                }
            }
            return sb.ToString();
        }

        // -----------------------------------------------------------------
        // Exclusions
        // -----------------------------------------------------------------

        private static void ParseExcludes(string excludeParam, out HashSet<string> excludeDirs, out List<Regex> excludeFileRegexes)
        {
            excludeDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            excludeFileRegexes = new List<Regex>();

            string[] entries;
            if (string.IsNullOrEmpty(excludeParam))
            {
                // Apply project-noise defaults
                entries = DefaultExcludes;
            }
            else
            {
                entries = excludeParam.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            }

            foreach (string entry in entries)
            {
                string e = entry.Trim();
                if (string.IsNullOrEmpty(e)) continue;

                // If it contains * or ?, treat as a filename glob exclusion
                if (e.IndexOfAny(new[] { '*', '?' }) >= 0)
                {
                    excludeFileRegexes.Add(new Regex("^" + GlobToRegex(e) + "$", RegexOptions.IgnoreCase));
                }
                else
                {
                    // Plain name -> directory exclusion
                    excludeDirs.Add(e);
                }
            }
        }

        // -----------------------------------------------------------------
        // Safe enumeration that skips excluded directories
        // -----------------------------------------------------------------

        private static IEnumerable<string> EnumerateFilesSafe(string root, SearchOption searchOption, HashSet<string> excludeDirs)
        {
            // We re-implement recursive walk so we can skip excluded directories entirely
            // instead of paying the cost of enumerating them and then filtering after.
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] files = null;
                try { files = System.IO.Directory.GetFiles(current); }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }

                foreach (var f in files) yield return f;

                if (searchOption == SearchOption.TopDirectoryOnly) continue;

                string[] subs = null;
                try { subs = System.IO.Directory.GetDirectories(current); }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }

                foreach (var sub in subs)
                {
                    string name = Path.GetFileName(sub);
                    if (excludeDirs.Contains(name)) continue;
                    stack.Push(sub);
                }
            }
        }

        private static IEnumerable<string> EnumerateDirectoriesSafe(string root, SearchOption searchOption, HashSet<string> excludeDirs)
        {
            var stack = new Stack<string>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                string current = stack.Pop();
                string[] subs = null;
                try { subs = System.IO.Directory.GetDirectories(current); }
                catch (UnauthorizedAccessException) { continue; }
                catch (DirectoryNotFoundException) { continue; }

                foreach (var sub in subs)
                {
                    string name = Path.GetFileName(sub);
                    if (excludeDirs.Contains(name)) continue;
                    if (current != root || searchOption == SearchOption.AllDirectories)
                        yield return sub;
                    if (searchOption == SearchOption.AllDirectories)
                        stack.Push(sub);
                    else if (current == root)
                        yield return sub;
                }
            }
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