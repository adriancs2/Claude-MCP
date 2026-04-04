using System;
using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

namespace MCP2.Core
{
    /// <summary>
    /// Validates that the MCP server is being called by Claude Desktop.
    /// Runs once at startup, not a service.
    /// </summary>
    public static class CallerValidator
    {
        // Claude Desktop path pattern: C:\Users\{current_user}\AppData\Local\AnthropicClaude\app-{n}.{n}.{n}\claude.exe
        private static readonly string ExpectedPathPattern = string.Format(
            @"^C:\\Users\\{0}\\AppData\\Local\\AnthropicClaude\\app-\d+\.\d+\.\d+\\claude\.exe$",
            Regex.Escape(Environment.UserName));

        // New MSIX path pattern: C:\Program Files\WindowsApps\Claude_{version}_{arch}__{hash}\app\Claude.exe
        private static readonly Regex ClaudeMsixPathPattern = new Regex(
            @"^C:\\Program Files\\WindowsApps\\Claude_[\d.]+_[a-z0-9]+__[a-z0-9]+\\app\\Claude\.exe$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex ClaudePathPattern = new Regex(
            ExpectedPathPattern,
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Validate that the parent (or grandparent) process is Claude Desktop.
        /// Throws UnauthorizedAccessException if validation fails.
        /// </summary>
        public static void Validate()
        {
            // Check for bypass environment variable (MCP_BYPASS_VALIDATION=1 or true)
            string bypass = Environment.GetEnvironmentVariable("MCP_BYPASS_VALIDATION");
            if (!string.IsNullOrEmpty(bypass) && (bypass == "1" || bypass.Equals("true", StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                int parentPid = GetParentProcessId(currentPid);

                if (parentPid == 0)
                    throw new UnauthorizedAccessException("Cannot determine parent process");

                // Check parent first
                string parentPath = GetProcessPath(parentPid);
                if (IsClaudeDesktop(parentPath))
                    return;

                // Claude may spawn via cmd.exe — check grandparent
                int grandparentPid = GetParentProcessId(parentPid);
                if (grandparentPid > 0)
                {
                    string grandparentPath = GetProcessPath(grandparentPid);
                    if (IsClaudeDesktop(grandparentPath))
                        return;
                }

                throw new UnauthorizedAccessException(
                    $"MCP server must be launched by Claude Desktop. Parent: {parentPath ?? "unknown"}");
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new UnauthorizedAccessException(
                    $"Failed to validate caller: {ex.Message}");
            }
        }

        /// <summary>
        /// Check if a process path matches Claude Desktop
        /// </summary>
        private static bool IsClaudeDesktop(string processPath)
        {
            if (string.IsNullOrEmpty(processPath))
                return false;

            if (ClaudeMsixPathPattern.IsMatch(processPath))
                return true;

            // Primary: regex match against expected install path
            if (ClaudePathPattern.IsMatch(processPath))
                return true;

            // Fallback: simple "claude" check for non-standard installs
            string lower = processPath.ToLowerInvariant();
            return lower.Contains("claude.exe") && lower.Contains("anthropic");
        }

        /// <summary>
        /// Get executable path for a process. Tries MainModule first, falls back to WMI.
        /// </summary>
        private static string GetProcessPath(int processId)
        {
            try
            {
                Process p = Process.GetProcessById(processId);
                return p.MainModule.FileName;
            }
            catch
            {
                // MainModule can fail for elevated/system processes, try WMI
                return GetProcessPathViaWmi(processId);
            }
        }

        /// <summary>
        /// Get executable path via WMI (fallback)
        /// </summary>
        private static string GetProcessPathViaWmi(int processId)
        {
            try
            {
                using (var query = new ManagementObjectSearcher(
                    $"SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {processId}"))
                {
                    using (var results = query.Get())
                    {
                        foreach (ManagementObject obj in results)
                        {
                            return obj["ExecutablePath"]?.ToString();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// Get the parent process ID using WMI
        /// </summary>
        private static int GetParentProcessId(int processId)
        {
            try
            {
                using (var query = new ManagementObjectSearcher(
                    $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}"))
                {
                    using (var results = query.Get())
                    {
                        foreach (ManagementObject obj in results)
                        {
                            return Convert.ToInt32(obj["ParentProcessId"]);
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}