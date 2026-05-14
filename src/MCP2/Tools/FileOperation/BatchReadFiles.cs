using System.Collections.Generic;
using System.IO;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.FileOperation
{
    /// <summary>
    /// Read multiple files in a single call. Each file entry can optionally specify
    /// a line range, so this one tool replaces the old batch_read_files +
    /// batch_read_files_ranges pair.
    /// </summary>
    public class BatchReadFiles : ITool
    {
        public string Name => "batch_read_files";

        public string Description =>
            "Read multiple files in a single call. Efficient for loading several files (or specific sections of them) at once. " +
            "Pass 'files' as an array — each entry is either a plain string path (read the whole file) or an object {path, start_line?, end_line?, label?} for ranged reads. " +
            "show_line_numbers prefixes every line with its number; ranged reads always show line numbers regardless of this flag.";

        public ToolParamList Params => new ToolParamList()
            .Array("files", "Array of file specifications. Each entry is either a string (full path, full file) or an object: { path: required, start_line?: int, end_line?: int, label?: string }.", required: true)
            .Bool("show_line_numbers", "Prefix every line with its line number for full-file reads. (Ranged reads always include line numbers.)", defaultValue: false)
            .StringEnum("format", "Output format: 'separated' (default, one block per file with headers), 'combined' (lightweight per-file headers), 'minimal' (no headers, just contents back-to-back).",
                new[] { "separated", "combined", "minimal" });

        public ToolResult Execute(JObject args)
        {
            JArray filesArray = args["files"] as JArray;
            if (filesArray == null || filesArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'files' array");

            bool showLineNumbers = args.Value<bool?>("show_line_numbers") ?? false;
            string format = args.Value<string>("format") ?? "separated";

            // ---- Parse and validate every file spec up front ----
            var specs = new List<FileSpec>();
            for (int i = 0; i < filesArray.Count; i++)
            {
                JToken token = filesArray[i];
                FileSpec spec;

                if (token.Type == JTokenType.String)
                {
                    spec = new FileSpec { Path = token.Value<string>() };
                }
                else if (token is JObject obj)
                {
                    spec = new FileSpec
                    {
                        Path = obj.Value<string>("path"),
                        StartLine = obj["start_line"]?.Value<int>(),
                        EndLine = obj["end_line"]?.Value<int>(),
                        Label = obj.Value<string>("label")
                    };
                }
                else
                {
                    return ToolResult.Error("INVALID_PARAMS", $"files[{i}] must be a string path or an object {{path, start_line?, end_line?}}");
                }

                if (string.IsNullOrEmpty(spec.Path))
                    return ToolResult.Error("INVALID_PARAMS", $"files[{i}] missing or empty path");

                if (!System.IO.File.Exists(spec.Path))
                    return ToolResult.Error($"File not found: {spec.Path}");

                specs.Add(spec);
            }

            // ---- Build output ----
            var output = new StringBuilder();

            for (int i = 0; i < specs.Count; i++)
            {
                FileSpec spec = specs[i];
                bool isRanged = spec.StartLine.HasValue || spec.EndLine.HasValue;
                bool isBinary = FileOperations.IsBinaryFile(spec.Path);

                // ---- Header ----
                if (format == "separated")
                {
                    output.AppendLine("==========================================");
                    string title = !string.IsNullOrEmpty(spec.Label)
                        ? spec.Label
                        : $"File {i + 1}/{specs.Count}: {Path.GetFileName(spec.Path)}";
                    output.AppendLine(title);
                    output.AppendLine($"Path: {spec.Path}");

                    if (!isBinary)
                    {
                        if (isRanged)
                        {
                            int s = spec.StartLine ?? 1;
                            int e = spec.EndLine ?? int.MaxValue;
                            output.AppendLine($"Range: lines {s}{(e == int.MaxValue ? "-end" : "-" + e)}");
                        }
                        else
                        {
                            int total = FileOperations.CountLines(spec.Path);
                            output.AppendLine($"Lines: {total}");
                        }
                    }

                    output.AppendLine("==========================================");
                    output.AppendLine();
                }
                else if (format == "combined")
                {
                    string suffix = isRanged
                        ? $" (lines {spec.StartLine ?? 1}-{(spec.EndLine.HasValue ? spec.EndLine.ToString() : "end")})"
                        : "";
                    string label = !string.IsNullOrEmpty(spec.Label) ? spec.Label : Path.GetFileName(spec.Path);
                    output.AppendLine($"# {label}{suffix}");
                }
                // 'minimal' emits no header

                // ---- Body ----
                if (isBinary)
                {
                    output.AppendLine(FileOperations.GetBinaryFileInfo(spec.Path));
                    if (format != "minimal") output.AppendLine();
                    continue;
                }

                string body;
                if (isRanged)
                {
                    // Ranged reads always include line numbers (more useful than not)
                    int s = spec.StartLine ?? 1;
                    int e = spec.EndLine ?? int.MaxValue;
                    if (s < 1) s = 1;
                    body = FileOperations.ReadLineRange(spec.Path, s, e);
                }
                else
                {
                    body = showLineNumbers
                        ? FileOperations.ReadFileWithLineNumbers(spec.Path)
                        : FileOperations.ReadFile(spec.Path);
                }

                output.AppendLine(body);
                if (format != "minimal") output.AppendLine();
            }

            // ---- Footer ----
            if (format == "separated")
            {
                output.AppendLine("==========================================");
                output.AppendLine($"Summary: Read {specs.Count} file(s)");
                output.AppendLine("==========================================");
            }

            return ToolResult.Success(output.ToString());
        }

        private class FileSpec
        {
            public string Path;
            public int? StartLine;
            public int? EndLine;
            public string Label;
        }
    }
}
