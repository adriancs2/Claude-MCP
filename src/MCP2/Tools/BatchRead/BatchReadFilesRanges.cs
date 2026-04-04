using System.Collections.Generic;
using System.IO;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.BatchRead
{
    public class BatchReadFilesRanges : ITool
    {
        public string Name => "batch_read_files_ranges";
        public string Description => "Read specific line ranges from multiple files in a single operation. Supports multiple ranges per file for precise extraction of code sections.";

        public ToolParamList Params => new ToolParamList()
            .Array("files", "Array of file specifications. Each item must be an object with: 'path' (string, required), 'ranges' (array of objects with 'start' and 'end' integers, optional - reads full file if omitted), 'label' (string, optional). Example: [{\"path\": \"file.txt\", \"ranges\": [{\"start\": 1, \"end\": 10}, {\"start\": 20, \"end\": 30}]}]", required: true)
            .Bool("include_line_numbers", "Prefix each line with its line number", defaultValue: true)
            .StringEnum("format", "Output format: 'separated' (default), 'combined', or 'minimal'",
                new[] { "separated", "combined", "minimal" });

        public ToolResult Execute(JObject args)
        {
            JArray filesArray = args.Value<JArray>("files");
            if (filesArray == null || filesArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'files' parameter");

            bool includeLineNumbers = args.Value<bool?>("include_line_numbers") ?? true;
            string format = args.Value<string>("format") ?? "separated";

            // Parse file specifications
            var fileSpecs = new List<FileSpec>();
            foreach (JToken token in filesArray)
            {
                JObject fileObj = token as JObject;
                if (fileObj == null)
                    return ToolResult.Error("INVALID_PARAMS", "Each file spec must be an object");

                string path = fileObj.Value<string>("path");
                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Missing 'path' in file spec");

                string label = fileObj.Value<string>("label");
                JToken rangesToken = fileObj["ranges"];

                if (!System.IO.File.Exists(path))
                    return ToolResult.Error($"File not found: {path}");

                var ranges = new List<LineRange>();
                if (rangesToken != null && rangesToken.Type == JTokenType.Array)
                {
                    foreach (JToken rangeToken in (JArray)rangesToken)
                    {
                        JObject rangeObj = rangeToken as JObject;
                        if (rangeObj == null) continue;
                        int start = rangeObj.Value<int>("start");
                        int end = rangeObj.Value<int>("end");
                        ranges.Add(new LineRange { Start = start, End = end });
                    }
                }

                fileSpecs.Add(new FileSpec { Path = path, Label = label, Ranges = ranges });
            }

            // Read and format output
            var output = new StringBuilder();
            int totalRanges = 0;

            for (int i = 0; i < fileSpecs.Count; i++)
            {
                var spec = fileSpecs[i];

                // Check for binary file
                if (FileOperations.IsBinaryFile(spec.Path))
                {
                    if (format == "separated")
                    {
                        output.AppendLine("==========================================");
                        output.AppendLine($"File {i + 1}/{fileSpecs.Count}: {spec.Label ?? Path.GetFileName(spec.Path)}");
                        output.AppendLine($"Path: {spec.Path}");
                        output.AppendLine("==========================================");
                        output.AppendLine();
                    }
                    output.AppendLine(FileOperations.GetBinaryFileInfo(spec.Path));
                    output.AppendLine();
                    continue;
                }

                string[] allLines = System.IO.File.ReadAllLines(spec.Path, Encoding.UTF8);

                // If no ranges specified, read entire file
                if (spec.Ranges.Count == 0)
                    spec.Ranges.Add(new LineRange { Start = 1, End = allLines.Length });

                // Validate ranges
                foreach (var range in spec.Ranges)
                {
                    if (range.Start < 1 || range.End > allLines.Length || range.Start > range.End)
                        return ToolResult.Error("LINE_OUT_OF_RANGE",
                            $"File {Path.GetFileName(spec.Path)}: Range {range.Start}-{range.End} is invalid (file has {allLines.Length} lines)");
                }

                // Format output
                if (format == "separated")
                {
                    output.AppendLine("==========================================");
                    output.AppendLine($"File {i + 1}/{fileSpecs.Count}: {spec.Label ?? Path.GetFileName(spec.Path)}");
                    output.AppendLine($"Path: {spec.Path}");

                    if (spec.Ranges.Count == 1 && spec.Ranges[0].Start == 1 && spec.Ranges[0].End == allLines.Length)
                    {
                        output.AppendLine("Ranges: [Full file]");
                    }
                    else
                    {
                        var rangeStrings = new List<string>();
                        foreach (var r in spec.Ranges)
                            rangeStrings.Add($"{r.Start}-{r.End}");
                        output.AppendLine($"Ranges: [{string.Join(", ", rangeStrings)}]");
                    }
                    output.AppendLine("==========================================");
                    output.AppendLine();
                }
                else if (format == "combined")
                {
                    var rangeStrings = new List<string>();
                    if (spec.Ranges.Count == 1 && spec.Ranges[0].Start == 1 && spec.Ranges[0].End == allLines.Length)
                    {
                        rangeStrings.Add("Full");
                    }
                    else
                    {
                        foreach (var r in spec.Ranges)
                            rangeStrings.Add($"{r.Start}-{r.End}");
                    }
                    output.AppendLine($"# {Path.GetFileName(spec.Path)} [{string.Join(", ", rangeStrings)}]");
                }

                // Output each range
                for (int r = 0; r < spec.Ranges.Count; r++)
                {
                    var range = spec.Ranges[r];
                    totalRanges++;

                    if (format == "separated")
                        output.AppendLine($"=== Range {r + 1}: Lines {range.Start}-{range.End} ===");

                    for (int lineNum = range.Start; lineNum <= range.End; lineNum++)
                    {
                        string line = allLines[lineNum - 1];
                        if (includeLineNumbers)
                            output.AppendLine($"{lineNum,5}| {line}");
                        else
                            output.AppendLine(line);
                    }

                    if (format == "separated")
                        output.AppendLine();
                }

                if (format != "minimal")
                    output.AppendLine();
            }

            if (format == "separated")
            {
                output.AppendLine("==========================================");
                output.AppendLine($"Summary: Read {fileSpecs.Count} file(s), {totalRanges} total range(s)");
                output.AppendLine("==========================================");
            }

            return ToolResult.Success(output.ToString());
        }

        private class FileSpec
        {
            public string Path { get; set; }
            public string Label { get; set; }
            public List<LineRange> Ranges { get; set; } = new List<LineRange>();
        }

        private class LineRange
        {
            public int Start { get; set; }
            public int End { get; set; }
        }
    }
}
