using System;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Ssh
{
    public class SshSend : ITool
    {
        public string Name => "ssh_send";
        public string Description => "Send a command to an open SSH session and return the output. The session must be opened first with ssh_open. The shell is interactive and persistent — environment variables, working directory (cd), and other state carry over between ssh_send calls within the same session. For long-running commands, increase timeout_ms.";

        public ToolParamList Params => new ToolParamList()
            .String("command", "The shell command to execute on the remote server", required: true)
            .String("profile", "Which SSH session (profile name) to send to. Default: 'default'")
            .Int("timeout_ms", "Maximum milliseconds to wait for output. Default: 10000 (10 seconds). Increase for slow commands.", defaultValue: 10000);

        public ToolResult Execute(JObject args)
        {
            string command = args.Value<string>("command");
            string profile = args.Value<string>("profile") ?? "default";
            int timeoutMs = args.Value<int?>("timeout_ms") ?? 10000;

            if (string.IsNullOrEmpty(command))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'command' parameter");

            if (timeoutMs < 500)
                timeoutMs = 500;
            if (timeoutMs > 120000)
                timeoutMs = 120000;

            var result = SshSessionManager.Send(profile, command, timeoutMs);

            if (!result.Success)
                return ToolResult.Error("SSH_SEND_FAILED", result.Error);

            string output = result.Output;

            // Truncate very large output
            bool truncated = false;
            if (output != null && output.Length > 50000)
            {
                output = output.Substring(0, 50000);
                truncated = true;
            }

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(output))
            {
                sb.Append(output);
                if (truncated)
                    sb.AppendLine("\n... [TRUNCATED at 50KB]");
            }
            else
            {
                sb.Append("(no output)");
            }

            return ToolResult.Success(sb.ToString());
        }
    }
}
