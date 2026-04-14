using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using Renci.SshNet;

namespace MCP2.Tools.Ssh
{
    public class SshDownload : ITool
    {
        public string Name => "ssh_download";
        public string Description => "Download remote files and/or folders from a server via SFTP. Accepts a mix of remote file and directory paths — directories are downloaded recursively. Opens a connection, transfers everything, then closes automatically. Does not require ssh_open.";

        public ToolParamList Params => new ToolParamList()
            .String("profile", "SSH profile name from mcp-config.json", required: true)
            .Array("paths", "Array of remote file or folder paths to download. Directories are downloaded recursively.", required: true)
            .String("destination", "Local destination folder path (e.g. D:\\downloads\\). Created if it doesn't exist.", required: true)
            .Bool("overwrite", "Overwrite existing local files. Default: true", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string profileName = args.Value<string>("profile");
            string destination = args.Value<string>("destination");
            bool overwrite = args.Value<bool?>("overwrite") ?? true;

            // Parse paths array
            var pathsToken = args["paths"] as JArray;
            if (pathsToken == null || pathsToken.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'paths' array.");

            var remotePaths = new List<string>();
            foreach (var token in pathsToken)
            {
                string p = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(p))
                    remotePaths.Add(p.Trim());
            }

            if (remotePaths.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "No valid paths provided.");

            if (string.IsNullOrEmpty(destination))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination' parameter.");

            // Resolve profile
            SshProfile profile;
            string profileError = SftpHelper.ResolveProfile(profileName, out profile);
            if (profileError != null)
                return ToolResult.Error("PROFILE_ERROR", profileError);

            // Ensure local destination exists
            if (!Directory.Exists(destination))
                Directory.CreateDirectory(destination);

            int fileCount = 0;
            int dirCount = 0;
            long totalBytes = 0;
            var errors = new List<string>();
            var sb = new StringBuilder();

            try
            {
                using (var sftp = SftpHelper.CreateClient(profile))
                {
                    sftp.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
                    sftp.Connect();

                    foreach (var remotePath in remotePaths)
                    {
                        string normalizedRemote = remotePath.Replace('\\', '/').TrimEnd('/');

                        try
                        {
                            var attrs = sftp.GetAttributes(normalizedRemote);

                            if (attrs.IsDirectory)
                            {
                                // Download directory recursively
                                string dirName = GetRemoteFileName(normalizedRemote);
                                string localDir = Path.Combine(destination, dirName);

                                var result = DownloadDirectory(sftp, normalizedRemote, localDir, overwrite);
                                fileCount += result.Files;
                                dirCount += result.Dirs;
                                totalBytes += result.Bytes;
                                errors.AddRange(result.Errors);
                            }
                            else
                            {
                                // Download single file
                                string fileName = GetRemoteFileName(normalizedRemote);
                                string localPath = Path.Combine(destination, fileName);

                                try
                                {
                                    long bytes = DownloadFile(sftp, normalizedRemote, localPath, overwrite);
                                    fileCount++;
                                    totalBytes += bytes;
                                }
                                catch (Exception ex)
                                {
                                    errors.Add($"  {normalizedRemote} -> {ex.Message}");
                                }
                            }
                        }
                        catch (Renci.SshNet.Common.SftpPathNotFoundException)
                        {
                            errors.Add($"  {normalizedRemote} -> Remote path not found");
                        }
                    }

                    sftp.Disconnect();
                }

                // Build result
                sb.AppendLine($"Download complete.");
                sb.AppendLine($"  Profile     : {profileName} ({profile.Host})");
                sb.AppendLine($"  Destination : {destination}");
                sb.AppendLine($"  Files       : {fileCount}");
                if (dirCount > 0)
                    sb.AppendLine($"  Directories : {dirCount}");
                sb.AppendLine($"  Total size  : {FormatBytes(totalBytes)}");

                if (errors.Count > 0)
                {
                    sb.AppendLine($"\nErrors ({errors.Count}):");
                    foreach (var e in errors)
                        sb.AppendLine(e);
                }

                return errors.Count > 0
                    ? ToolResult.Error("PARTIAL_DOWNLOAD", sb.ToString())
                    : ToolResult.Success(sb.ToString());
            }
            catch (Exception ex)
            {
                return ToolResult.Error("SFTP_ERROR", $"SFTP connection/download failed: {ex.Message}");
            }
        }

        private long DownloadFile(SftpClient sftp, string remotePath, string localPath, bool overwrite)
        {
            if (!overwrite && File.Exists(localPath))
                return 0;

            // Ensure local directory exists
            string dir = Path.GetDirectoryName(localPath);
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            using (var fs = File.Create(localPath))
            {
                sftp.DownloadFile(remotePath, fs);
                return fs.Length;
            }
        }

        private DownloadResult DownloadDirectory(SftpClient sftp, string remoteDir, string localDir, bool overwrite)
        {
            var result = new DownloadResult();

            if (!Directory.Exists(localDir))
                Directory.CreateDirectory(localDir);
            result.Dirs++;

            // List remote directory contents
            foreach (var entry in sftp.ListDirectory(remoteDir))
            {
                // Skip . and ..
                if (entry.Name == "." || entry.Name == "..")
                    continue;

                string remotePath = remoteDir + "/" + entry.Name;
                string localPath = Path.Combine(localDir, entry.Name);

                if (entry.IsDirectory)
                {
                    var subResult = DownloadDirectory(sftp, remotePath, localPath, overwrite);
                    result.Files += subResult.Files;
                    result.Dirs += subResult.Dirs;
                    result.Bytes += subResult.Bytes;
                    result.Errors.AddRange(subResult.Errors);
                }
                else if (entry.IsRegularFile)
                {
                    try
                    {
                        long bytes = DownloadFile(sftp, remotePath, localPath, overwrite);
                        result.Files++;
                        result.Bytes += bytes;
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add($"  {remotePath} -> {ex.Message}");
                    }
                }
                // Skip symlinks, sockets, etc.
            }

            return result;
        }

        private string GetRemoteFileName(string remotePath)
        {
            int lastSlash = remotePath.LastIndexOf('/');
            return lastSlash >= 0 ? remotePath.Substring(lastSlash + 1) : remotePath;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private class DownloadResult
        {
            public int Files;
            public int Dirs;
            public long Bytes;
            public List<string> Errors = new List<string>();
        }
    }
}
