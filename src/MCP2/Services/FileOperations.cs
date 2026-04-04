using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using MCP2.Core;

namespace MCP2.Services
{
    /// <summary>
    /// Core file operations with line-number support and path validation.
    /// Handles encoding preservation, binary file detection, and comprehensive editing operations.
    /// </summary>
    public static class FileOperations
    {
        private const string LineEnding = "\r\n";
        
        // UTF-8 encoding without BOM (Byte Order Mark) - default for new files
        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        
        // UTF-8 encoding with BOM - used when original file has BOM
        private static readonly Encoding Utf8WithBom = new UTF8Encoding(true);

        /// <summary>
        /// Known binary file extensions that should never be read as text
        /// </summary>
        private static readonly HashSet<string> BinaryExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Executables and libraries
            ".exe", ".dll", ".so", ".dylib", ".bin", ".com", ".sys", ".ocx",
            // Images
            ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".ico", ".tiff", ".tif", ".webp", ".svg", ".psd", ".ai", ".raw",
            // Audio
            ".mp3", ".wav", ".flac", ".aac", ".ogg", ".wma", ".m4a", ".aiff",
            // Video
            ".mp4", ".avi", ".mkv", ".mov", ".wmv", ".flv", ".webm", ".m4v", ".mpeg", ".mpg",
            // Archives
            ".zip", ".rar", ".7z", ".tar", ".gz", ".bz2", ".xz", ".cab", ".iso",
            // Documents (binary formats)
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
            // Fonts
            ".ttf", ".otf", ".woff", ".woff2", ".eot",
            // Database
            ".db", ".sqlite", ".mdb", ".accdb",
            // .NET / Java
            ".pdb", ".nupkg", ".jar", ".class", ".war", ".ear",
            // Other binary
            ".obj", ".o", ".a", ".lib", ".res", ".cache", ".suo", ".user"
        };

        #region Encoding Detection

        /// <summary>
        /// Detects if a file has UTF-8 BOM (Byte Order Mark)
        /// BOM bytes: EF BB BF
        /// </summary>
        private static bool HasUtf8Bom(string path)
        {
            try
            {
                byte[] bom = new byte[3];
                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    if (fs.Length < 3)
                        return false;
                    fs.Read(bom, 0, 3);
                }
                return bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the appropriate encoding for writing back to a file.
        /// Preserves BOM if original file had it, otherwise uses UTF-8 without BOM.
        /// </summary>
        private static Encoding GetWriteEncoding(string path)
        {
            if (!File.Exists(path))
                return Utf8NoBom;
            
            return HasUtf8Bom(path) ? Utf8WithBom : Utf8NoBom;
        }

        #endregion

        #region Binary File Detection

        /// <summary>
        /// Checks if a file is binary by extension first, then by content analysis
        /// </summary>
        public static bool IsBinaryFile(string path)
        {
            // Check extension first (fast path)
            string ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && BinaryExtensions.Contains(ext))
            {
                return true;
            }

            // Content-based detection: check for null bytes in first 8KB
            try
            {
                byte[] buffer = new byte[8192];
                int bytesRead;

                using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    bytesRead = fs.Read(buffer, 0, buffer.Length);
                }

                // Check for null bytes (strong indicator of binary content)
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                // If we can't read, assume text (will fail gracefully later)
                return false;
            }
        }

        /// <summary>
        /// Generates a summary info string for binary files instead of content
        /// </summary>
        public static string GetBinaryFileInfo(string path)
        {
            FileInfo fi = new FileInfo(path);
            StringBuilder sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine("  BINARY FILE - Cannot display as text");
            sb.AppendLine("═══════════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine(string.Format("  File:      {0}", fi.Name));
            sb.AppendLine(string.Format("  Path:      {0}", fi.FullName));
            sb.AppendLine(string.Format("  Extension: {0}", fi.Extension));
            sb.AppendLine(string.Format("  Size:      {0:N0} bytes ({1})", fi.Length, FormatFileSize(fi.Length)));
            sb.AppendLine(string.Format("  Modified:  {0:yyyy-MM-dd HH:mm:ss}", fi.LastWriteTime));
            sb.AppendLine(string.Format("  Created:   {0:yyyy-MM-dd HH:mm:ss}", fi.CreationTime));
            sb.AppendLine();
            sb.AppendLine("  This file type cannot be read as text. Use appropriate tools:");
            sb.AppendLine(GetToolSuggestion(fi.Extension));
            sb.AppendLine();
            sb.AppendLine("═══════════════════════════════════════════════════════════════");

            return sb.ToString();
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

        private static string GetToolSuggestion(string extension)
        {
            extension = (extension ?? "").ToLowerInvariant();

            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || 
                extension == ".gif" || extension == ".bmp" || extension == ".ico" || extension == ".webp")
                return "    → Image file: Use image viewer or graphics editor";
            
            if (extension == ".pdf")
                return "    → PDF document: Use PDF reader";
            
            if (extension == ".docx" || extension == ".doc")
                return "    → Word document: Use Microsoft Word or compatible editor";
            
            if (extension == ".xlsx" || extension == ".xls")
                return "    → Excel spreadsheet: Use Microsoft Excel or compatible editor";
            
            if (extension == ".pptx" || extension == ".ppt")
                return "    → PowerPoint: Use Microsoft PowerPoint";
            
            if (extension == ".zip" || extension == ".rar" || extension == ".7z" || extension == ".tar" || extension == ".gz")
                return "    → Archive file: Use archive extraction tools";
            
            if (extension == ".exe" || extension == ".dll")
                return "    → Executable/Library: Use appropriate .NET tools or decompiler";
            
            if (extension == ".mp3" || extension == ".wav" || extension == ".flac" || extension == ".m4a")
                return "    → Audio file: Use media player";
            
            if (extension == ".mp4" || extension == ".avi" || extension == ".mkv" || extension == ".mov")
                return "    → Video file: Use media player";
            
            if (extension == ".db" || extension == ".sqlite")
                return "    → SQLite database: Use sqlite tools";

            return "    → Use appropriate application for this file type";
        }

        #endregion

        #region Reading Operations

        /// <summary>
        /// Reads complete file content as raw text
        /// </summary>
        public static string ReadFile(string path)
        {
            ValidateFileExists(path);
            
            if (IsBinaryFile(path))
            {
                return GetBinaryFileInfo(path);
            }
            
            return File.ReadAllText(path, Encoding.UTF8);
        }

        /// <summary>
        /// Reads file and returns content with line numbers prefixed
        /// </summary>
        public static string ReadFileWithLineNumbers(string path)
        {
            ValidateFileExists(path);

            if (IsBinaryFile(path))
            {
                return GetBinaryFileInfo(path);
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            StringBuilder sb = new StringBuilder();
            int lineNumWidth = lines.Length.ToString().Length;

            for (int i = 0; i < lines.Length; i++)
            {
                string lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                sb.AppendFormat("{0}| {1}", lineNum, lines[i]);
                if (i < lines.Length - 1)
                {
                    sb.Append(LineEnding);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Reads a specific range of lines with line numbers
        /// </summary>
        public static string ReadLineRange(string path, int startLine, int endLine)
        {
            ValidateFileExists(path);

            if (IsBinaryFile(path))
            {
                return GetBinaryFileInfo(path);
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            int totalLines = lines.Length;

            if (startLine < 1) startLine = 1;

            // Clamp end_line to file length
            if (endLine > totalLines) endLine = totalLines;

            // If start_line is beyond file length, return informational message
            if (startLine > totalLines)
            {
                return string.Format("[File has {0} lines, requested start_line {1} is beyond end of file]", totalLines, startLine);
            }

            // If end_line < start_line after clamping, nothing to return
            if (endLine < startLine)
            {
                return string.Format("[No lines in range: start_line={0}, end_line={1}, file has {2} lines]", startLine, endLine, totalLines);
            }

            StringBuilder sb = new StringBuilder();
            int lineNumWidth = endLine.ToString().Length;

            for (int i = startLine - 1; i < endLine; i++)
            {
                string lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                sb.AppendFormat("{0}| {1}", lineNum, lines[i]);
                if (i < endLine - 1)
                {
                    sb.Append(LineEnding);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets the total line count of a file. Returns -1 for binary files.
        /// </summary>
        public static int CountLines(string path)
        {
            ValidateFileExists(path);
            
            if (IsBinaryFile(path))
            {
                return -1;
            }
            
            return File.ReadAllLines(path, Encoding.UTF8).Length;
        }

        /// <summary>
        /// Finds all lines matching a pattern
        /// </summary>
        public static string FindPattern(string path, string pattern, bool caseSensitive = false)
        {
            ValidateFileExists(path);

            if (IsBinaryFile(path))
            {
                return GetBinaryFileInfo(path);
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            StringBuilder sb = new StringBuilder();
            int matchCount = 0;
            int lineNumWidth = lines.Length.ToString().Length;

            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(pattern, comparison) >= 0)
                {
                    string lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                    sb.AppendFormat("{0}| {1}", lineNum, lines[i]);
                    sb.Append(LineEnding);
                    matchCount++;
                }
            }

            if (matchCount == 0)
            {
                return string.Format("PATTERN_NOT_FOUND: No matches found for '{0}'", pattern);
            }

            sb.Append(LineEnding);
            sb.AppendFormat("Found {0} occurrence(s).", matchCount);
            return sb.ToString();
        }

        /// <summary>
        /// Finds all occurrences with surrounding context
        /// </summary>
        public static string FindAllOccurrences(string path, string pattern, int contextLines = 2, bool caseSensitive = false)
        {
            ValidateFileExists(path);

            if (IsBinaryFile(path))
            {
                return GetBinaryFileInfo(path);
            }

            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            List<int> matchingLines = new List<int>();
            int lineNumWidth = lines.Length.ToString().Length;

            StringComparison comparison = caseSensitive 
                ? StringComparison.Ordinal 
                : StringComparison.OrdinalIgnoreCase;

            // Find all matching line indices
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(pattern, comparison) >= 0)
                {
                    matchingLines.Add(i);
                }
            }

            if (matchingLines.Count == 0)
            {
                return string.Format("PATTERN_NOT_FOUND: No matches found for '{0}'", pattern);
            }

            StringBuilder sb = new StringBuilder();
            
            for (int occNum = 0; occNum < matchingLines.Count; occNum++)
            {
                int matchLine = matchingLines[occNum];
                int startContext = Math.Max(0, matchLine - contextLines);
                int endContext = Math.Min(lines.Length - 1, matchLine + contextLines);

                sb.AppendFormat("=== Occurrence #{0} at line {1} ===", occNum + 1, matchLine + 1);
                sb.Append(LineEnding);

                for (int i = startContext; i <= endContext; i++)
                {
                    string lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                    string marker = (i == matchLine) ? "  // <-- MATCH" : "";
                    sb.AppendFormat("{0}| {1}{2}", lineNum, lines[i], marker);
                    sb.Append(LineEnding);
                }

                sb.Append(LineEnding);
            }

            sb.AppendFormat("Total: {0} occurrence(s) found.", matchingLines.Count);
            return sb.ToString();
        }

        #endregion

        #region Writing Operations

        /// <summary>
        /// Writes content to a file, creating it if it doesn't exist.
        /// Uses UTF-8 with BOM by default.
        /// </summary>
        public static void WriteFile(string path, string content, bool useBom = true)
        {
            // Ensure parent directory exists
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var encoding = useBom ? Utf8WithBom : Utf8NoBom;
            File.WriteAllText(path, content, encoding);
        }


        /// <summary>
        /// Appends content to the end of a file, preserving encoding.
        /// </summary>
        public static void AppendToFile(string path, string content)
        {
            ValidateFileExists(path);

            File.AppendAllText(path, content, GetWriteEncoding(path));
        }

        #endregion

        #region Line Editing Operations

        /// <summary>
        /// Replaces a single line in a file.
        /// Returns null on success, or an informational message if out of range.
        /// </summary>
        public static string EditLine(string path, int lineNum, string newContent)
        {
            ValidateFileExists(path);

            if (lineNum < 1)
                throw new ArgumentException("Line number must be >= 1");

            var encoding = GetWriteEncoding(path);
            var lines = File.ReadAllLines(path, encoding);

            if (lineNum > lines.Length)
                return string.Format("Line {0} is beyond end of file (file has {1} lines). No changes made.", lineNum, lines.Length);

            lines[lineNum - 1] = newContent;
            File.WriteAllText(path, string.Join(LineEnding, lines), encoding);
            return null;
        }

        /// <summary>
        /// Replaces a range of lines with new content.
        /// Returns null on success, or an informational message if out of range.
        /// </summary>
        public static string EditLineRange(string path, int startLine, int endLine, string newContent)
        {
            ValidateFileExists(path);

            if (startLine < 1)
                throw new ArgumentException("Start line must be >= 1");

            if (endLine < startLine)
                throw new ArgumentException("End line must be >= start line");

            var encoding = GetWriteEncoding(path);
            var lines = File.ReadAllLines(path, encoding).ToList();

            if (startLine > lines.Count)
                return string.Format("Start line {0} is beyond end of file (file has {1} lines). No changes made.", startLine, lines.Count);

            // Clamp endLine to file length
            endLine = Math.Min(endLine, lines.Count);

            // Remove the range
            lines.RemoveRange(startLine - 1, endLine - startLine + 1);

            // Insert new content
            var newLines = newContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            lines.InsertRange(startLine - 1, newLines);

            File.WriteAllText(path, string.Join(LineEnding, lines), encoding);
            return null;
        }

        /// <summary>
        /// Inserts content before a specific line.
        /// Returns null on success, or an informational message if line was clamped.
        /// </summary>
        public static string InsertAtLine(string path, int lineNum, string content)
        {
            ValidateFileExists(path);

            if (lineNum < 1)
                throw new ArgumentException("Line number must be >= 1");

            var encoding = GetWriteEncoding(path);
            var lines = File.ReadAllLines(path, encoding).ToList();

            string note = null;

            // If beyond file end, append at end
            if (lineNum > lines.Count + 1)
            {
                note = string.Format("Line {0} is beyond end of file (file has {1} lines). Content appended at end.", lineNum, lines.Count);
                lineNum = lines.Count + 1;
            }

            var newLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            lines.InsertRange(lineNum - 1, newLines);

            File.WriteAllText(path, string.Join(LineEnding, lines), encoding);
            return note;
        }

        /// <summary>
        /// Inserts content after a specific line.
        /// Returns null on success, or an informational message if line was clamped.
        /// </summary>
        public static string InsertAfterLine(string path, int lineNum, string content)
        {
            ValidateFileExists(path);

            if (lineNum < 1)
                throw new ArgumentException("Line number must be >= 1");

            var encoding = GetWriteEncoding(path);
            var lines = File.ReadAllLines(path, encoding).ToList();

            string note = null;

            // If beyond file end, append at end
            if (lineNum > lines.Count)
            {
                note = string.Format("Line {0} is beyond end of file (file has {1} lines). Content appended at end.", lineNum, lines.Count);
                lineNum = lines.Count;
            }

            var newLines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            lines.InsertRange(lineNum, newLines);

            File.WriteAllText(path, string.Join(LineEnding, lines), encoding);
            return note;
        }

        /// <summary>
        /// Deletes a range of lines from a file.
        /// Returns null on success, or an informational message if out of range.
        /// </summary>
        public static string DeleteLines(string path, int startLine, int endLine)
        {
            ValidateFileExists(path);

            if (startLine < 1)
                throw new ArgumentException("Start line must be >= 1");

            if (endLine < startLine)
                throw new ArgumentException("End line must be >= start line");

            var encoding = GetWriteEncoding(path);
            var lines = File.ReadAllLines(path, encoding).ToList();

            // If start_line is beyond file, nothing to delete
            if (startLine > lines.Count)
                return string.Format("Delete range {0}-{1} is beyond end of file (file has {2} lines). No changes made.", startLine, endLine, lines.Count);

            // Clamp endLine to file length
            endLine = Math.Min(endLine, lines.Count);

            lines.RemoveRange(startLine - 1, endLine - startLine + 1);

            File.WriteAllText(path, string.Join(LineEnding, lines), encoding);
            return null;
        }

        #endregion

        #region Pattern Replacement Operations

        /// <summary>
        /// Replaces the Nth occurrence of a pattern in a file
        /// </summary>
        public static void EditNthOccurrence(string path, string pattern, int n, string replacement, bool caseSensitive = false)
        {
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            if (n < 1)
                throw new ArgumentException("N must be >= 1");

            var encoding = GetWriteEncoding(path);
            var content = File.ReadAllText(path, encoding);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var index = -1;
            var currentOccurrence = 0;

            while ((index = content.IndexOf(pattern, index + 1, comparison)) >= 0)
            {
                currentOccurrence++;
                if (currentOccurrence == n)
                {
                    content = content.Substring(0, index) + replacement + content.Substring(index + pattern.Length);
                    File.WriteAllText(path, content, encoding);
                    return;
                }
            }

            throw new InvalidOperationException(string.Format("Pattern '{0}' does not have {1} occurrence(s) in file", pattern, n));
        }

        /// <summary>
        /// Replaces all occurrences of a pattern within a specific line range
        /// </summary>
        public static void ReplaceInLineRange(string path, int startLine, int endLine, string pattern, string replacement, bool caseSensitive = false)
        {
            
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            if (startLine < 1)
                throw new ArgumentException("Start line must be >= 1");

            if (endLine < startLine)
                throw new ArgumentException("End line must be >= start line");

            var encoding = GetWriteEncoding(path);
            var lines = File.ReadAllLines(path, encoding);

            if (startLine > lines.Length)
                throw new ArgumentException(string.Format("Start line {0} does not exist (file has {1} lines)", startLine, lines.Length));

            endLine = Math.Min(endLine, lines.Length);

            for (int i = startLine - 1; i < endLine; i++)
            {
                if (caseSensitive)
                    lines[i] = lines[i].Replace(pattern, replacement);
                else
                    lines[i] = Regex.Replace(lines[i], Regex.Escape(pattern), replacement, RegexOptions.IgnoreCase);
            }

            File.WriteAllText(path, string.Join(LineEnding, lines), encoding);
        }

        /// <summary>
        /// Replaces text using a regular expression pattern
        /// </summary>
        public static void ReplaceRegex(string path, string pattern, string replacement)
        {
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            var encoding = GetWriteEncoding(path);
            var content = File.ReadAllText(path, encoding);

            try
            {
                content = Regex.Replace(content, pattern, replacement);
                File.WriteAllText(path, content, encoding);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException(string.Format("Invalid regex pattern: {0}", ex.Message), ex);
            }
        }

        /// <summary>
        /// Replaces all occurrences of a pattern in the entire file
        /// </summary>
        public static void ReplaceAll(string path, string pattern, string replacement, bool caseSensitive = false)
        {
            
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            var encoding = GetWriteEncoding(path);
            var content = File.ReadAllText(path, encoding);

            if (caseSensitive)
                content = content.Replace(pattern, replacement);
            else
                content = Regex.Replace(content, Regex.Escape(pattern), replacement, RegexOptions.IgnoreCase);

            File.WriteAllText(path, content, encoding);
        }

        /// <summary>
        /// Replaces the first occurrence of a pattern in a file
        /// </summary>
        public static void ReplaceFirst(string path, string pattern, string replacement, bool caseSensitive = false)
        {
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            var encoding = GetWriteEncoding(path);
            var content = File.ReadAllText(path, encoding);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var index = content.IndexOf(pattern, comparison);
            if (index >= 0)
            {
                content = content.Substring(0, index) + replacement + content.Substring(index + pattern.Length);
                File.WriteAllText(path, content, encoding);
            }
        }

        #endregion

        #region Batch Safety

        /// <summary>
        /// Sorts line-based edits bottom-to-top per file for safe batch processing
        /// </summary>
        public static List<(string path, int lineNum, string content)> SortEditsBottomUp(
            List<(string path, int lineNum, string content)> edits)
        {
            return edits
                .OrderBy(e => e.path, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => e.lineNum)
                .ToList();
        }

        /// <summary>
        /// Sorts range-based edits bottom-to-top per file for safe batch processing
        /// </summary>
        public static List<(string path, int startLine, int endLine, string content)> SortRangeEditsBottomUp(
            List<(string path, int startLine, int endLine, string content)> edits)
        {
            return edits
                .OrderBy(e => e.path, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(e => e.startLine)
                .ToList();
        }

        #endregion

        #region Counted Replacement Operations

        /// <summary>
        /// Replaces all occurrences and returns the count of replacements made
        /// </summary>
        public static int ReplaceAllCounted(string path, string pattern, string replacement, bool caseSensitive = false)
        {
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            var encoding = GetWriteEncoding(path);
            var content = File.ReadAllText(path, encoding);

            // Count occurrences first
            int count = 0;
            int idx = 0;
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            while ((idx = content.IndexOf(pattern, idx, comparison)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }

            if (count == 0)
                return 0;

            // Perform replacement
            if (caseSensitive)
                content = content.Replace(pattern, replacement);
            else
                content = Regex.Replace(content, Regex.Escape(pattern), replacement, RegexOptions.IgnoreCase);

            File.WriteAllText(path, content, encoding);
            return count;
        }

        /// <summary>
        /// Replaces the first occurrence and returns the line number where it was found (1-based), or 0 if not found
        /// </summary>
        public static int ReplaceFirstCounted(string path, string pattern, string replacement, bool caseSensitive = false)
        {
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            var encoding = GetWriteEncoding(path);
            var content = File.ReadAllText(path, encoding);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            var index = content.IndexOf(pattern, comparison);
            if (index >= 0)
            {
                // Calculate line number from character index
                int lineNumber = 1;
                for (int i = 0; i < index; i++)
                {
                    if (content[i] == '\n')
                        lineNumber++;
                }

                content = content.Substring(0, index) + replacement + content.Substring(index + pattern.Length);
                File.WriteAllText(path, content, encoding);
                return lineNumber;
            }

            return 0;
        }

        /// <summary>
        /// Counts the number of occurrences of a pattern in a file without modifying it.
        /// Used by replace_first's must_be_unique check.
        /// </summary>
        public static int CountOccurrences(string path, string pattern, bool caseSensitive = true)
        {
            ValidateFileExists(path);

            if (string.IsNullOrEmpty(pattern))
                throw new ArgumentException("Pattern cannot be empty");

            var content = File.ReadAllText(path, Encoding.UTF8);
            var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

            int count = 0;
            int idx = 0;
            while ((idx = content.IndexOf(pattern, idx, comparison)) >= 0)
            {
                count++;
                idx += pattern.Length;
            }

            return count;
        }


        #endregion


        #region Helper Methods

        private static void ValidateFileExists(string path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(string.Format("FILE_NOT_FOUND: {0}", path));
            }
        }

        #endregion
    }
}