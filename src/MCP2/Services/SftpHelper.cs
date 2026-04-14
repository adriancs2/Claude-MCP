using System;
using System.IO;
using Renci.SshNet;
using MCP2.Core;

namespace MCP2.Services
{
    /// <summary>
    /// Creates short-lived SftpClient connections from SSH profiles.
    /// Each call opens, uses, and closes the connection — no persistent state.
    /// </summary>
    public static class SftpHelper
    {
        /// <summary>
        /// Resolve an SSH profile by name from McpConfig.
        /// Returns null on success (profile written to out param), or an error message.
        /// </summary>
        public static string ResolveProfile(string profileName, out SshProfile profile)
        {
            profile = null;

            if (string.IsNullOrEmpty(profileName))
                return "Missing 'profile' parameter.";

            if (McpConfig.SshProfiles == null || McpConfig.SshProfiles.Count == 0)
                return "No SSH profiles found in mcp-config.json. Add an 'ssh_profiles' section.";

            if (!McpConfig.SshProfiles.TryGetValue(profileName, out profile))
            {
                string available = string.Join(", ", McpConfig.SshProfiles.Keys);
                return $"SSH profile '{profileName}' not found.\nAvailable profiles: {available}";
            }

            if (string.IsNullOrEmpty(profile.Host))
                return $"Profile '{profileName}' is missing 'host'.";
            if (string.IsNullOrEmpty(profile.Username))
                return $"Profile '{profileName}' is missing 'username'.";

            return null; // success
        }

        /// <summary>
        /// Create an SftpClient from a profile. Caller must Connect() and Dispose().
        /// </summary>
        public static SftpClient CreateClient(SshProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.PrivateKeyPath))
            {
                PrivateKeyFile keyFile;
                if (!string.IsNullOrEmpty(profile.Passphrase))
                    keyFile = new PrivateKeyFile(profile.PrivateKeyPath, profile.Passphrase);
                else
                    keyFile = new PrivateKeyFile(profile.PrivateKeyPath);

                return new SftpClient(profile.Host, profile.Port, profile.Username, keyFile);
            }
            else
            {
                return new SftpClient(profile.Host, profile.Port, profile.Username, profile.Password);
            }
        }

        /// <summary>
        /// Ensure a remote directory exists, creating parent directories as needed.
        /// </summary>
        public static void EnsureRemoteDirectory(SftpClient sftp, string remotePath)
        {
            if (string.IsNullOrEmpty(remotePath) || remotePath == "/" || remotePath == ".")
                return;

            // Normalize to forward slashes
            remotePath = remotePath.Replace('\\', '/').TrimEnd('/');

            // Split into parts and build incrementally
            string[] parts = remotePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            string current = remotePath.StartsWith("/") ? "" : ".";

            foreach (var part in parts)
            {
                current = current == "" ? "/" + part : current + "/" + part;

                try
                {
                    var attrs = sftp.GetAttributes(current);
                    if (!attrs.IsDirectory)
                        throw new Exception($"Remote path '{current}' exists but is not a directory.");
                }
                catch (Renci.SshNet.Common.SftpPathNotFoundException)
                {
                    sftp.CreateDirectory(current);
                }
            }
        }
    }
}
