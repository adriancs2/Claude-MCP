using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class FindAllOccurrences : ITool
    {
        public string Name => "find_all_occurrences";
        public string Description => "Find all occurrences of a pattern with surrounding context lines. Use this before edit_nth_occurrence to identify which occurrence number to target.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("pattern", "Text pattern to search for", required: true)
            .Int("context_lines", "Number of context lines before and after each match", defaultValue: 2)
            .Bool("case_sensitive", "Perform case-sensitive search", defaultValue: false);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string pattern = args.Value<string>("pattern");
            int contextLines = args.Value<int?>("context_lines") ?? 2;
            bool caseSensitive = args.Value<bool?>("case_sensitive") ?? false;

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (string.IsNullOrEmpty(pattern))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'pattern' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            string result = FileOperations.FindAllOccurrences(path, pattern, contextLines, caseSensitive);
            return ToolResult.Success(result);
        }
    }
}