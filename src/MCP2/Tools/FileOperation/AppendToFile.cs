using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.FileOperation
{
    public class AppendToFile : ITool
    {
        public string Name => "append_to_file";
        public string Description => "Append content to the end of an existing file. Returns a unified diff of the appended content.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the file", required: true)
            .String("content", "Content to append to the file", required: true);

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            string content = args.Value<string>("content");

            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");
            if (content == null)
                return ToolResult.Error("INVALID_PARAMS", "Missing 'content' parameter");

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            // Mandatory auto-backup before modifying file
            var backupService = new BackupService();
            string backupPath = backupService.CreateBackup(path);
            string backupInfo = $"\nBackup created: {System.IO.Path.GetFileName(backupPath)}";

            // Snapshot before appending so we can show the diff.
            string before = System.IO.File.ReadAllText(path, Encoding.UTF8);

            FileOperations.AppendToFile(path, content);

            string after = System.IO.File.ReadAllText(path, Encoding.UTF8);
            string diff = UnifiedDiff.ForEdit(path, before, after);

            StringBuilder result = new StringBuilder();
            result.AppendLine($"Content appended successfully to: {path}{backupInfo}");
            result.AppendLine();
            result.Append(diff);

            return ToolResult.Success(result.ToString());
        }
    }
}
