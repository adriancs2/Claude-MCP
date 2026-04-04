using System.Collections.Generic;
using System.IO;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.BatchRead
{
    public class BatchReadFiles : ITool
    {
        public string Name => "batch_read_files";
        public string Description => "Read multiple complete files in a single operation. Efficient for loading several files at once.";

        public ToolParamList Params => new ToolParamList()
            .Array("paths", "Array of file paths to read", required: true)
            .Bool("include_line_numbers", "Prefix each line with its line number", defaultValue: false)
            .StringEnum("format", "Output format: 'separated' (default), 'combined', or 'minimal'",
                new[] { "separated", "combined", "minimal" });

        public ToolResult Execute(JObject args)
        {
            JArray pathsArray = args.Value<JArray>("paths");
            if (pathsArray == null || pathsArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'paths' parameter");

            bool includeLineNumbers = args.Value<bool?>("include_line_numbers") ?? false;
            string format = args.Value<string>("format") ?? "separated";

            // Parse and validate all paths first
            var paths = new List<string>();
            foreach (JToken pathToken in pathsArray)
            {
                string path = pathToken.Value<string>();
                if (string.IsNullOrEmpty(path))
                    return ToolResult.Error("INVALID_PARAMS", "Empty path in array");

                if (!System.IO.File.Exists(path))
                    return ToolResult.Error($"File not found: {path}");

                paths.Add(path);
            }

            // Read and format output
            var output = new StringBuilder();

            for (int i = 0; i < paths.Count; i++)
            {
                string path = paths[i];

                // Check for binary file
                if (FileOperations.IsBinaryFile(path))
                {
                    if (format == "separated")
                    {
                        output.AppendLine("==========================================");
                        output.AppendLine($"File {i + 1}/{paths.Count}: {Path.GetFileName(path)}");
                        output.AppendLine($"Path: {path}");
                        output.AppendLine("==========================================");
                        output.AppendLine();
                    }
                    output.AppendLine(FileOperations.GetBinaryFileInfo(path));
                    output.AppendLine();
                    continue;
                }

                string[] lines = System.IO.File.ReadAllLines(path, Encoding.UTF8);

                if (format == "separated")
                {
                    output.AppendLine("==========================================");
                    output.AppendLine($"File {i + 1}/{paths.Count}: {Path.GetFileName(path)}");
                    output.AppendLine($"Path: {path}");
                    output.AppendLine($"Lines: {lines.Length}");
                    output.AppendLine("==========================================");
                    output.AppendLine();
                }
                else if (format == "combined")
                {
                    output.AppendLine($"# {Path.GetFileName(path)} ({lines.Length} lines)");
                }

                for (int lineNum = 0; lineNum < lines.Length; lineNum++)
                {
                    if (includeLineNumbers)
                        output.AppendLine($"{lineNum + 1,5}| {lines[lineNum]}");
                    else
                        output.AppendLine(lines[lineNum]);
                }

                if (format != "minimal")
                    output.AppendLine();
            }

            if (format == "separated")
            {
                output.AppendLine("==========================================");
                output.AppendLine($"Summary: Read {paths.Count} file(s)");
                output.AppendLine("==========================================");
            }

            return ToolResult.Success(output.ToString());
        }
    }
}
