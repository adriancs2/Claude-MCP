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
    public class SshUpload : ITool
    {
        public string Name => "ssh_upload";
        public string Description => "Upload local files and/or folders to a remote server via SFTP. Accepts a mix of file paths and directory paths — directories are uploaded recursively. Opens a connection, transfers everything, then closes automatically. Does not require ssh_open.";

        public ToolParamList Params => new ToolParamList()
            .String("profile", "SSH profile name from mcp-config.json", required: true)
            .Array("paths", "Array of local file or folder paths to upload. Directories are uploaded recursively.", required: true)
            .String("destination", "Remote destination folder path (e.g. /home/user/project/). Created if it doesn't exist.", required: true)
            .Bool("overwrite", "Overwrite existing remote files. Default: true", defaultValue: true);

        public ToolResult Execute(JObject args)
        {
            string profileName = args.Value<string>("profile");
            string destination = args.Value<string>("destination");
            bool overwrite = args.Value<bool?>("overwrite") ?? true;

            // Parse paths array
            var pathsToken = args["paths"] as JArray;
            if (pathsToken == null || pathsToken.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'paths' array.");

            var localPaths = new List<string>();
            foreach (var token in pathsToken)
            {
                string p = token.Value<string>();
                if (!string.IsNullOrWhiteSpace(p))
                    localPaths.Add(p.Trim());
            }

            if (localPaths.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "No valid paths provided.");

            if (string.IsNullOrEmpty(destination))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'destination' parameter.");

            // Resolve profile
            SshProfile profile;
            string profileError = SftpHelper.ResolveProfile(profileName, out profile);
            if (profileError != null)
                return ToolResult.Error("PROFILE_ERROR", profileError);

            // Validate all local paths exist
            foreach (var p in localPaths)
            {
                if (!System.IO.File.Exists(p) && !System.IO.Directory.Exists(p))
                    return ToolResult.Error("PATH_NOT_FOUND", $"Local path not found: {p}");
            }

            // Normalize destination
            destination = destination.Replace('\\', '/');
            if (!destination.EndsWith("/"))
                destination += "/";

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

                    // Ensure base destination exists
                    SftpHelper.EnsureRemoteDirectory(sftp, destination.TrimEnd('/'));

                    foreach (var localPath in localPaths)
                    {
                        if (System.IO.File.Exists(localPath))
                        {
                            // Single file
                            string fileName = System.IO.Path.GetFileName(localPath);
                            string remotePath = destination + fileName;

                            try
                            {
                                long bytes = UploadFile(sftp, localPath, remotePath, overwrite);
                                fileCount++;
                                totalBytes += bytes;
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"  {localPath} -> {ex.Message}");
                            }
                        }
                        else if (System.IO.Directory.Exists(localPath))
                        {
                            // Directory — upload recursively
                            string dirName = System.IO.Path.GetFileName(localPath.TrimEnd('\\', '/'));
                            string remoteDir = destination + dirName;

                            try
                            {
                                var result = UploadDirectory(sftp, localPath, remoteDir, overwrite);
                                fileCount += result.Files;
                                dirCount += result.Dirs;
                                totalBytes += result.Bytes;
                                errors.AddRange(result.Errors);
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"  {localPath}/ -> {ex.Message}");
                            }
                        }
                    }

                    sftp.Disconnect();
                }

                // Build result
                sb.AppendLine($"Upload complete.");
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
                    ? ToolResult.Error("PARTIAL_UPLOAD", sb.ToString())
                    : ToolResult.Success(sb.ToString());
            }
            catch (Exception ex)
            {
                return ToolResult.Error("SFTP_ERROR", $"SFTP connection/upload failed: {ex.Message}");
            }
        }

        private long UploadFile(SftpClient sftp, string localPath, string remotePath, bool overwrite)
        {
            // Check if file exists remotely
            if (!overwrite)
            {
                try
                {
                    sftp.GetAttributes(remotePath);
                    // File exists and overwrite is false — skip
                    return 0;
                }
                catch (Renci.SshNet.Common.SftpPathNotFoundException) { }
            }

            using (var fs = System.IO.File.OpenRead(localPath))
            {
                sftp.UploadFile(fs, remotePath, true);
                return fs.Length;
            }
        }

        private UploadResult UploadDirectory(SftpClient sftp, string localDir, string remoteDir, bool overwrite)
        {
            var result = new UploadResult();

            SftpHelper.EnsureRemoteDirectory(sftp, remoteDir);
            result.Dirs++;

            // Upload files in this directory
            foreach (var filePath in System.IO.Directory.GetFiles(localDir))
            {
                string fileName = System.IO.Path.GetFileName(filePath);
                string remotePath = remoteDir + "/" + fileName;

                try
                {
                    long bytes = UploadFile(sftp, filePath, remotePath, overwrite);
                    result.Files++;
                    result.Bytes += bytes;
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"  {filePath} -> {ex.Message}");
                }
            }

            // Recurse into subdirectories
            foreach (var subDir in System.IO.Directory.GetDirectories(localDir))
            {
                string dirName = System.IO.Path.GetFileName(subDir);
                string remoteSubDir = remoteDir + "/" + dirName;

                var subResult = UploadDirectory(sftp, subDir, remoteSubDir, overwrite);
                result.Files += subResult.Files;
                result.Dirs += subResult.Dirs;
                result.Bytes += subResult.Bytes;
                result.Errors.AddRange(subResult.Errors);
            }

            return result;
        }

        private string FormatBytes(long bytes)
        {
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        private class UploadResult
        {
            public int Files;
            public int Dirs;
            public long Bytes;
            public List<string> Errors = new List<string>();
        }
    }
}
