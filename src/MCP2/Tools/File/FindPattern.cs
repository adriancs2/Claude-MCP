using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Text;

namespace MCP2.Tools.File
{
    public class FindPattern : ITool
    {
        public string Name => "find_pattern";
        public string Description => "Find all lines in a file that match a text pattern. Returns matching lines with line numbers. For context around matches, use find_all_occurrences instead.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("pattern", "Text pattern to search for", required: true)
            .Bool("case_sensitive", "Perform case-sensitive search", defaultValue: false)
            .Int("max_results", "Maximum number of matches to return (0 = all)", defaultValue: 0);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string pattern = args.Value<string>("pattern");
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? false;
            int maxResults = args.Value<int?>("max_results") ?? 0;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(pattern))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'pattern' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error(string.Format("File not found: {0}", path));

            if (FileOperations.IsBinaryFile(path))
                return ToolResult.Success(FileOperations.GetBinaryFileInfo(path));

            string[] lines = System.IO.File.ReadAllLines(path, Encoding.UTF8);
            StringBuilder sb = new StringBuilder();
            int matchCount = 0;
            int totalMatches = 0;
            int lineNumWidth = lines.Length.ToString().Length;

            StringComparison comparison = caseSensitive
                ? StringComparison.Ordinal
                : StringComparison.OrdinalIgnoreCase;

            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].IndexOf(pattern, comparison) >= 0)
                {
                    totalMatches++;

                    if (maxResults <= 0 || matchCount < maxResults)
                    {
                        string lineNum = (i + 1).ToString().PadLeft(lineNumWidth);
                        sb.AppendFormat("{0}| {1}\r\n", lineNum, lines[i]);
                        matchCount++;
                    }
                }
            }

            if (totalMatches == 0)
            {
                return ToolResult.Success(string.Format("PATTERN_NOT_FOUND: No matches found for '{0}'", pattern));
            }

            sb.AppendLine();

            if (maxResults > 0 && totalMatches > maxResults)
            {
                sb.AppendFormat("Showing {0} of {1} match(es). ({2} more not shown)", 
                    matchCount, totalMatches, totalMatches - matchCount);
            }
            else
            {
                sb.AppendFormat("Found {0} match(es).", totalMatches);
            }

            return ToolResult.Success(sb.ToString());
        }
    }
}