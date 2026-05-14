using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.FileOperation
{
    /// <summary>
    /// Unified file read. Supports full-file or partial-range reads, with or without
    /// line-number prefixes. Replaces the older read_file / read_file_lines /
    /// read_line_range trio with a single tool whose mode is controlled by parameters.
    /// </summary>
    public class ReadFile : ITool
    {
        public string Name => "read_file";

        public string Description =>
            "Read a file's contents. " +
            "By default returns the full file as raw text. " +
            "Pass show_line_numbers=true to prefix every line with its line number — useful when you plan to follow up with line-targeted edits. " +
            "Pass start_line and/or end_line to read only a range (line numbers are always shown for ranged reads, since you almost always want them). " +
            "Out-of-range line numbers clamp gracefully — start_line beyond file length returns an info message, end_line is clamped to the actual file length.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .Bool("show_line_numbers", "Prefix every line with its line number. Defaults to false for full-file reads, true (always) for ranged reads.", defaultValue: false)
            .Int("start_line", "First line to read (1-based, inclusive). If omitted, reads from the beginning.")
            .Int("end_line", "Last line to read (1-based, inclusive). If omitted, reads to the end of the file.");

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            bool showLineNumbers = args.Value<bool?>("show_line_numbers") ?? false;
            int? startLine = args.Value<int?>("start_line");
            int? endLine = args.Value<int?>("end_line");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            // Ranged read — line numbers are always shown for ranges
            if (startLine.HasValue || endLine.HasValue)
            {
                int s = startLine ?? 1;
                int e = endLine ?? int.MaxValue;
                if (s < 1) s = 1;

                string ranged = FileOperations.ReadLineRange(path, s, e);
                return ToolResult.Success(ranged);
            }

            // Full-file read
            string content = showLineNumbers
                ? FileOperations.ReadFileWithLineNumbers(path)
                : FileOperations.ReadFile(path);
            return ToolResult.Success(content);
        }
    }
}
