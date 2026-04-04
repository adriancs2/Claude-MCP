using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System.Text;

namespace MCP2.Tools.Directory
{
    /// <summary>
    /// Show all directories that are configured as allowed
    /// </summary>
    public class ListAllowedDirectories : ITool
    {
        public string Name => "list_allowed_directories";
        public string Description => "Show all directories that are configured as allowed.";

        public ToolParamList Params => new ToolParamList();

        public ToolResult Execute(JObject args)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Allowed directories:");
            foreach (string dir in McpConfig.AllowedDirectories)
            {
                sb.AppendLine(string.Format("  {0}", dir));
            }
            return ToolResult.Success(sb.ToString());
        }
    }
}
