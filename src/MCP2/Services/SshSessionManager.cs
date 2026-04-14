using System;
using System.Linq;
using System.Collections;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using Renci.SshNet;

namespace MCP2.Services
{
    /// <summary>
    /// Manages persistent SSH connections and shell streams.
    /// Thread-safe singleton — sessions are keyed by a user-chosen session_id.
    /// </summary>
    public static class SshSessionManager
    {
        private static readonly ConcurrentDictionary<string, SshSession> _sessions
            = new ConcurrentDictionary<string, SshSession>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Open a new SSH session. Returns error message if failed, null on success.
        /// </summary>
        public static string Open(string sessionId, string host, int port, string username,
            string password, string privateKeyPath, string passphrase)
        {
            if (_sessions.ContainsKey(sessionId))
                return $"Session '{sessionId}' is already open. Close it first or use a different session_id.";

            try
            {
                SshClient client;

                if (!string.IsNullOrEmpty(privateKeyPath))
                {
                    // Key-based auth
                    if (!File.Exists(privateKeyPath))
                        return $"Private key file not found: {privateKeyPath}";

                    PrivateKeyFile keyFile;
                    if (!string.IsNullOrEmpty(passphrase))
                        keyFile = new PrivateKeyFile(privateKeyPath, passphrase);
                    else
                        keyFile = new PrivateKeyFile(privateKeyPath);

                    client = new SshClient(host, port, username, keyFile);
                }
                else if (!string.IsNullOrEmpty(password))
                {
                    // Password auth
                    client = new SshClient(host, port, username, password);
                }
                else
                {
                    return "Either 'password' or 'private_key_path' must be provided.";
                }

                client.ConnectionInfo.Timeout = TimeSpan.FromSeconds(15);
                client.Connect();

                if (!client.IsConnected)
                {
                    client.Dispose();
                    return "SSH connection failed — client reports not connected after Connect().";
                }

                // Create an interactive shell stream
                var shell = client.CreateShellStream("mcp2-terminal", 200, 50, 800, 600, 65536);

                // Wait briefly for the initial login banner / prompt
                Thread.Sleep(1500);

                var session = new SshSession
                {
                    Client = client,
                    Shell = shell,
                    Host = host,
                    Port = port,
                    Username = username,
                    ConnectedAt = DateTime.Now
                };

                _sessions[sessionId] = session;
                return null; // success
            }
            catch (Exception ex)
            {
                return $"SSH connection failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Send a command to an open shell stream and read the response.
        /// </summary>
        public static SshSendResult Send(string sessionId, string command, int timeoutMs)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
                return SshSendResult.Fail($"Session '{sessionId}' not found. Use ssh_open first.");

            if (!session.Client.IsConnected)
            {
                Close(sessionId);
                return SshSendResult.Fail($"Session '{sessionId}' is disconnected. It has been removed.");
            }

            try
            {
                var shell = session.Shell;

                // Drain any leftover data before sending
                if (shell.DataAvailable)
                    shell.Read();

                // Write the command + newline
                shell.WriteLine(command);

                // Wait for output to arrive
                var sb = new StringBuilder();
                int waited = 0;
                int pollInterval = 100;
                int quietTime = 0;
                int quietThreshold = 500; // consider output "done" after 500ms of silence

                while (waited < timeoutMs)
                {
                    Thread.Sleep(pollInterval);
                    waited += pollInterval;

                    if (shell.DataAvailable)
                    {
                        string chunk = shell.Read();
                        sb.Append(chunk);
                        quietTime = 0; // reset silence timer
                    }
                    else
                    {
                        quietTime += pollInterval;
                        if (quietTime >= quietThreshold && sb.Length > 0)
                            break; // output arrived and then went quiet — done
                    }
                }

                string output = sb.ToString();

                // Trim the echoed command from the top if present
                output = TrimEchoedCommand(output, command);

                // Trim the trailing shell prompt (e.g. "user@host:~$ ")
                output = TrimTrailingPrompt(output, session.Username);

                return SshSendResult.Ok(output.TrimEnd());
            }
            catch (Exception ex)
            {
                return SshSendResult.Fail($"Error sending command: {ex.Message}");
            }
        }

        /// <summary>
        /// Close and dispose an SSH session.
        /// </summary>
        public static string Close(string sessionId)
        {
            if (!_sessions.TryRemove(sessionId, out var session))
                return $"Session '{sessionId}' not found.";

            try
            {
                session.Shell?.Dispose();
                if (session.Client.IsConnected)
                    session.Client.Disconnect();
                session.Client.Dispose();
            }
            catch { }

            return null; // success
        }

        /// <summary>
        /// List all open sessions.
        /// </summary>
        public static SshSession[] ListSessions()
        {
            return _sessions.Values.ToArray();
        }

        /// <summary>
        /// Check if a session exists and is connected.
        /// </summary>
        public static bool IsConnected(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var s) && s.Client.IsConnected;
        }

        public static string[] GetSessionIds()
        {
            return _sessions.Keys.ToArray();
        }

        /// <summary>
        /// Remove the echoed command line from shell output.
        /// Shell streams echo the command back; we strip it for cleaner output.
        /// </summary>
        private static string TrimEchoedCommand(string output, string command)
        {
            if (string.IsNullOrEmpty(output))
                return output;

            // Split into lines and try to find + remove the echoed command
            var lines = output.Split(new[] { '\n' }, StringSplitOptions.None);
            int startIndex = 0;

            for (int i = 0; i < Math.Min(lines.Length, 3); i++)
            {
                // The echoed line often contains the command (possibly with prompt prefix)
                if (lines[i].TrimEnd('\r').Contains(command.TrimEnd()))
                {
                    startIndex = i + 1;
                    break;
                }
            }

            if (startIndex > 0 && startIndex < lines.Length)
            {
                return string.Join("\n", lines, startIndex, lines.Length - startIndex);
            }

            return output;
        }

        /// <summary>
        /// Remove the trailing shell prompt line from output.
        /// Pattern: "username@...$ " or "username@...:...$ " at the last line.
        /// </summary>
        private static string TrimTrailingPrompt(string output, string username)
        {
            if (string.IsNullOrEmpty(output) || string.IsNullOrEmpty(username))
                return output;

            // Split into lines, walk backwards to find and remove trailing prompt lines
            var lines = output.Split(new[] { '\n' }, StringSplitOptions.None);
            int endIndex = lines.Length;

            // Walk backwards past empty lines, then check for prompt
            while (endIndex > 0)
            {
                string line = lines[endIndex - 1].TrimEnd('\r').TrimEnd();

                // Skip empty trailing lines
                if (string.IsNullOrEmpty(line))
                {
                    endIndex--;
                    continue;
                }

                // Check if this line looks like a shell prompt: "username@...$ "
                if (line.StartsWith(username + "@") && (line.EndsWith("$") || line.EndsWith("#")))
                {
                    endIndex--;
                    break; // only strip one prompt line
                }

                break; // non-empty, non-prompt line — stop
            }

            if (endIndex < lines.Length && endIndex > 0)
            {
                return string.Join("\n", lines, 0, endIndex);
            }

            return output;
        }
    }

    /// <summary>
    /// Holds an open SSH connection and its shell stream.
    /// </summary>
    public class SshSession
    {
        public SshClient Client { get; set; }
        public ShellStream Shell { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public DateTime ConnectedAt { get; set; }
    }

    /// <summary>
    /// Result of an ssh_send operation.
    /// </summary>
    public class SshSendResult
    {
        public bool Success { get; set; }
        public string Output { get; set; }
        public string Error { get; set; }

        public static SshSendResult Ok(string output) =>
            new SshSendResult { Success = true, Output = output };

        public static SshSendResult Fail(string error) =>
            new SshSendResult { Success = false, Error = error };
    }
}