using System;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Ssh
{
    public class SshClose : ITool
    {
        public string Name => "ssh_close";
        public string Description => "Close an open SSH session and release resources. Pass the profile name used in ssh_open. Use 'all' to close every open session.";

        public ToolParamList Params => new ToolParamList()
            .String("profile", "Which session (profile name) to close. Default: 'default'. Use 'all' to close all open sessions.");

        public ToolResult Execute(JObject args)
        {
            string profile = args.Value<string>("profile") ?? "default";

            if (profile.Equals("all", StringComparison.OrdinalIgnoreCase))
            {
                var ids = SshSessionManager.GetSessionIds();
                if (ids.Length == 0)
                    return ToolResult.Success("No open SSH sessions to close.");

                var sb = new StringBuilder();
                sb.AppendLine($"Closing {ids.Length} session(s):");
                foreach (var id in ids)
                {
                    string err = SshSessionManager.Close(id);
                    if (err != null)
                        sb.AppendLine($"  {id}: {err}");
                    else
                        sb.AppendLine($"  {id}: closed");
                }
                return ToolResult.Success(sb.ToString());
            }
            else
            {
                string error = SshSessionManager.Close(profile);
                if (error != null)
                    return ToolResult.Error("SSH_CLOSE_FAILED", error);

                return ToolResult.Success($"SSH session '{profile}' closed.");
            }
        }
    }
}
