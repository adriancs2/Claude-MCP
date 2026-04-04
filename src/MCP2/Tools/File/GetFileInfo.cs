using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.File
{
    public class GetFileInfo : ITool
    {
        public string Name => "get_file_info";
        public string Description => "Get detailed information about a file.";
        
        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error(string.Format("File not found: {0}", path));

            if (FileOperations.IsBinaryFile(path))
            {
                return ToolResult.Success(FileOperations.GetBinaryFileInfo(path));
            }

            var fi = new System.IO.FileInfo(path);
            var sb = new System.Text.StringBuilder();
            
            sb.AppendLine(string.Format("File: {0}", fi.Name));
            sb.AppendLine(string.Format("Path: {0}", fi.FullName));
            sb.AppendLine(string.Format("Size: {0:N0} bytes", fi.Length));
            sb.AppendLine(string.Format("Extension: {0}", fi.Extension));
            sb.AppendLine(string.Format("Created: {0:yyyy-MM-dd HH:mm:ss}", fi.CreationTime));
            sb.AppendLine(string.Format("Modified: {0:yyyy-MM-dd HH:mm:ss}", fi.LastWriteTime));
            sb.AppendLine(string.Format("Accessed: {0:yyyy-MM-dd HH:mm:ss}", fi.LastAccessTime));
            sb.AppendLine(string.Format("Read-only: {0}", fi.IsReadOnly));
            sb.AppendLine(string.Format("Attributes: {0}", fi.Attributes));
            
            int lineCount = FileOperations.CountLines(path);
            if (lineCount >= 0)
                sb.AppendLine(string.Format("Lines: {0}", lineCount));

            return ToolResult.Success(sb.ToString());
        }
    }
}