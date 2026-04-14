using System;
using System.Linq;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Ssh
{
    public class SshOpen : ITool
    {
        public string Name => "ssh_open";
        public string Description => "Open a persistent SSH connection using a named profile from mcp-config.json. The connection stays alive across multiple ssh_send calls until explicitly closed with ssh_close. The profile name also serves as the session_id for ssh_send and ssh_close. Available profiles: " + GetProfileList();

        public ToolParamList Params => new ToolParamList()
            .String("profile", "Name of the SSH profile defined in mcp-config.json under 'ssh_profiles'. This also becomes the session_id for ssh_send and ssh_close.", required: true);

        public ToolResult Execute(JObject args)
        {
            string profileName = args.Value<string>("profile");

            if (string.IsNullOrEmpty(profileName))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'profile' parameter");

            // Look up the profile
            if (McpConfig.SshProfiles == null || McpConfig.SshProfiles.Count == 0)
                return ToolResult.Error("NO_PROFILES", "No SSH profiles found in mcp-config.json. Add an 'ssh_profiles' section.");

            if (!McpConfig.SshProfiles.TryGetValue(profileName, out var profile))
            {
                string available = string.Join(", ", McpConfig.SshProfiles.Keys);
                return ToolResult.Error("PROFILE_NOT_FOUND",
                    $"SSH profile '{profileName}' not found.\nAvailable profiles: {available}");
            }

            if (string.IsNullOrEmpty(profile.Host))
                return ToolResult.Error("INVALID_PROFILE", $"Profile '{profileName}' is missing 'host'.");
            if (string.IsNullOrEmpty(profile.Username))
                return ToolResult.Error("INVALID_PROFILE", $"Profile '{profileName}' is missing 'username'.");

            // Use the profile name as the session_id
            string error = SshSessionManager.Open(
                profileName,
                profile.Host,
                profile.Port,
                profile.Username,
                profile.Password,
                profile.PrivateKeyPath,
                profile.Passphrase);

            if (error != null)
                return ToolResult.Error("SSH_CONNECT_FAILED", error);

            var sb = new StringBuilder();
            sb.AppendLine($"SSH connection established.");
            sb.AppendLine($"  Profile : {profileName}");
            sb.AppendLine($"  Host    : {profile.Host}:{profile.Port}");
            sb.AppendLine($"  User    : {profile.Username}");
            sb.AppendLine($"  Auth    : {(string.IsNullOrEmpty(profile.PrivateKeyPath) ? "password" : "private key")}");
            sb.AppendLine();
            sb.AppendLine("Use ssh_send to execute commands. Use ssh_close when done.");

            return ToolResult.Success(sb.ToString());
        }

        private static string GetProfileList()
        {
            if (McpConfig.SshProfiles == null || McpConfig.SshProfiles.Count == 0)
                return "(none configured)";
            return string.Join(", ", McpConfig.SshProfiles.Keys.Select(k => "'" + k + "'"));
        }
    }
}
