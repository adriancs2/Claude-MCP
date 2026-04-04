using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class WriteFile : ITool
    {
        public string Name => "write_file";
        public string Description => "Write content to a file, creating it if it doesn't exist or overwriting if it does. Default encoding: UTF-8 with BOM (ensures correct non-ASCII rendering in browsers).";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("content", "Content to write to the file", required: true)
            .String("encoding", "Encoding: 'utf8-bom' (default) or 'utf8' (without BOM)", required: false);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string content = args.Value<string>("content");
            string encoding = args.Value<string>("encoding");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (content == null)
                return ToolResult.Error("INVALID_PARAMS", "Missing 'content' parameter");

            

            bool useBom = true; // default: UTF-8 with BOM
            if (!string.IsNullOrEmpty(encoding) && encoding.Trim().ToLower() == "utf8")
                useBom = false;

            FileOperations.WriteFile(path, content, useBom);
            
            var fi = new System.IO.FileInfo(path);
            int lineCount = 0;
            if (content.Length > 0)
            {
                lineCount = 1;
                for (int i = 0; i < content.Length; i++)
                {
                    if (content[i] == '\n') lineCount++;
                }
            }

            return ToolResult.Success(string.Format(
                "File written: {0}\nSize: {1:N0} bytes, {2} line(s)", 
                System.IO.Path.GetFileName(path), fi.Length, lineCount));
        }
    }
}