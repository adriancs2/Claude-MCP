using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MCP2.Core;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Shell
{
    public class RunCommand : ITool
    {
        public string Name => "run_command";
        public string Description => "Execute an external program (cmd, powershell, or any executable) and return the output. stdout and stderr are captured directly in C# via stream redirection (not shell redirection), so output is always returned reliably. Three ways to run PowerShell: (1) 'parameters' for simple single-line commands, (2) 'script' for inline multi-line scripts (auto-creates and cleans up a temp .ps1 file), (3) 'script_path' for an existing .ps1 file on disk. The 'script' param is the PREFERRED approach — just paste your PowerShell code directly, no escaping needed, no temp file management. For non-PowerShell programs, use 'program' + 'parameters'.";

        public ToolParamList Params => new ToolParamList()
            .String("program", "Filename or full path to the executable (e.g., 'powershell', 'cmd', 'ipconfig', 'pnputil')", required: true)
            .String("parameters", "Command-line arguments for single-line execution. For PowerShell, do NOT include -Command or -File — just the command string. For commands with $variables or complex quoting, use 'script' instead.")
            .String("script", "Inline script content (multi-line supported). For PowerShell: auto-written to a temp .ps1 file, executed with -NoProfile -ExecutionPolicy Bypass, then deleted. For cmd: auto-written to a temp .bat file, then deleted. No escaping needed — just paste your script code directly. This is the PREFERRED approach for anything beyond a trivial one-liner.")
            .String("script_path", "Full path to an existing script file (e.g., .ps1) on disk. Use 'script' instead if you want to pass the script content inline without pre-creating a file.")
            .String("report_log", "Full path to a text file where stdout will be written. The file content is automatically returned to you. Default: auto-generated temp file.")
            .String("working_directory", "Working directory for the process. If not specified, inherits from the MCP2 server process.")
            .Bool("wait_for_exit", "Wait for the process to finish before returning. Default: true", defaultValue: true)
            .Int("timeout_seconds", "Maximum seconds to wait for the process. Default: 30", defaultValue: 30);

        public ToolResult Execute(JObject args)
        {
            string program = args.Value<string>("program");
            if (string.IsNullOrEmpty(program))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'program' parameter");

            string parameters = args.Value<string>("parameters") ?? "";
            string script = args.Value<string>("script");
            string scriptPath = args.Value<string>("script_path");
            string reportLog = args.Value<string>("report_log");
            string workingDirectory = args.Value<string>("working_directory");
            bool waitForExit = args.Value<bool?>("wait_for_exit") ?? true;
            int timeoutSeconds = args.Value<int?>("timeout_seconds") ?? 30;

            // If inline script content is provided, write it to a temp file
            string autoScriptPath = null;
            if (!string.IsNullOrEmpty(script))
            {
                string ext = IsPowerShell(program) ? ".ps1" : IsCmd(program) ? ".bat" : ".sh";
                autoScriptPath = Path.Combine(Path.GetTempPath(), $"mcp2_script_{Guid.NewGuid():N}{ext}");
                System.IO.File.WriteAllText(autoScriptPath, script, new UTF8Encoding(false));
                scriptPath = autoScriptPath;
            }

            // Validate script_path if provided
            if (!string.IsNullOrEmpty(scriptPath))
            {
                if (!System.IO.File.Exists(scriptPath))
                    return ToolResult.Error("FILE_NOT_FOUND", $"Script file not found: {scriptPath}");
            }

            // Auto-generate report log path if not specified
            bool autoLog = string.IsNullOrEmpty(reportLog);
            if (autoLog)
            {
                reportLog = Path.Combine(Path.GetTempPath(), $"mcp2_cmd_{Guid.NewGuid():N}.log");
            }

            try
            {
                // NOTE: autoScriptPath cleanup is in the finally block below
                // Build final program and arguments
                // FIX: Do NOT use shell-string redirection (> or *>).
                // Instead, redirect stdout/stderr via ProcessStartInfo and write to log in C#.
                // Shell redirection strings only work when UseShellExecute=true,
                // but UseShellExecute=true prevents redirecting streams in C#.
                // Solution: always use UseShellExecute=false + RedirectStandardOutput/Error,
                // then write captured output to the log file ourselves.

                string finalProgram;
                string finalArgs;

                if (!string.IsNullOrEmpty(scriptPath) && IsPowerShell(program))
                {
                    // PowerShell script file
                    finalProgram = program;
                    finalArgs = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"";
                }
                else if (IsPowerShell(program))
                {
                    // PowerShell inline command
                    finalProgram = program;
                    finalArgs = $"-NoProfile -ExecutionPolicy Bypass -Command \"{EscapeForPowerShellArg(parameters)}\"";
                }
                else if (IsCmd(program))
                {
                    // CMD
                    finalProgram = program;
                    finalArgs = $"/c {parameters}";
                }
                else if (!string.IsNullOrEmpty(scriptPath))
                {
                    // Generic executable with script file (e.g., python script.py)
                    finalProgram = program;
                    finalArgs = $"\"{scriptPath}\" {parameters}".Trim();
                }
                else
                {
                    // Generic executable
                    finalProgram = program;
                    finalArgs = parameters;
                }

                var psi = new ProcessStartInfo
                {
                    FileName = finalProgram,
                    Arguments = finalArgs,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,   // FIX: capture stdout in C#
                    RedirectStandardError = true,     // FIX: capture stderr in C#
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                if (!string.IsNullOrEmpty(workingDirectory) && System.IO.Directory.Exists(workingDirectory))
                    psi.WorkingDirectory = workingDirectory;

                var stdoutSb = new StringBuilder();
                var stderrSb = new StringBuilder();
                var resultSb = new StringBuilder();
                int exitCode = 0;

                using (var process = new Process { StartInfo = psi })
                {
                    // FIX: Read stdout/stderr asynchronously to prevent deadlocks
                    // when the child process fills its output buffer.
                    process.OutputDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            stdoutSb.AppendLine(e.Data);
                    };
                    process.ErrorDataReceived += (sender, e) =>
                    {
                        if (e.Data != null)
                            stderrSb.AppendLine(e.Data);
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    if (waitForExit)
                    {
                        bool exited = process.WaitForExit(timeoutSeconds * 1000);

                        // FIX: Call WaitForExit() with no arguments a second time after the
                        // timed overload to ensure async stream readers fully flush.
                        // Without this, the last lines of output may be missing.
                        if (exited)
                            process.WaitForExit();

                        if (!exited)
                        {
                            try { process.Kill(); } catch { }

                            // FIX: Include any output captured before timeout
                            // so the caller can debug what happened.
                            var timeoutResult = new StringBuilder();
                            timeoutResult.AppendLine($"Error [TIMEOUT]: Process did not exit within {timeoutSeconds} seconds.");

                            string partialOut = stdoutSb.ToString().Trim();
                            string partialErr = stderrSb.ToString().Trim();

                            if (!string.IsNullOrEmpty(partialOut))
                            {
                                timeoutResult.AppendLine("--- Partial STDOUT ---");
                                // Cap at 20KB to avoid huge payloads
                                if (partialOut.Length > 20000)
                                    partialOut = partialOut.Substring(0, 20000) + "\n... [TRUNCATED]";
                                timeoutResult.AppendLine(partialOut);
                            }
                            if (!string.IsNullOrEmpty(partialErr))
                            {
                                timeoutResult.AppendLine("--- Partial STDERR ---");
                                if (partialErr.Length > 20000)
                                    partialErr = partialErr.Substring(0, 20000) + "\n... [TRUNCATED]";
                                timeoutResult.AppendLine(partialErr);
                            }

                            return ToolResult.Error("TIMEOUT", timeoutResult.ToString());
                        }

                        exitCode = process.ExitCode;
                    }
                    else
                    {
                        resultSb.AppendLine("Process launched (not waiting for exit).");
                        resultSb.AppendLine($"Output will be written to: {reportLog}");
                        return ToolResult.Success(resultSb.ToString());
                    }
                }

                // Write captured output to the log file
                string stdout = stdoutSb.ToString();
                string stderr = stderrSb.ToString();

                string combinedOutput = stdout;
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    combinedOutput += (combinedOutput.Length > 0 ? "\n" : "") + "--- STDERR ---\n" + stderr;
                }

                // Always write log file (even if empty, so caller knows it was created)
                System.IO.File.WriteAllText(reportLog, combinedOutput, new UTF8Encoding(false));

                // Build result
                resultSb.AppendLine($"Exit Code: {exitCode}");

                string outputToReturn = combinedOutput.Trim();

                // Truncate if extremely large (> 50KB)
                bool truncated = false;
                if (outputToReturn.Length > 50000)
                {
                    outputToReturn = outputToReturn.Substring(0, 50000);
                    truncated = true;
                }

                if (!string.IsNullOrEmpty(outputToReturn))
                {
                    resultSb.AppendLine("--- Output ---");
                    resultSb.AppendLine(outputToReturn);
                    if (truncated)
                        resultSb.AppendLine($"\n... [TRUNCATED - full output saved to: {reportLog}]");
                }
                else
                {
                    resultSb.AppendLine("(no output)");
                }

                // Clean up auto-generated temp log files
                if (autoLog)
                {
                    try { System.IO.File.Delete(reportLog); } catch { }
                }

                return ToolResult.Success(resultSb.ToString());
            }
            catch (Exception ex)
            {
                return ToolResult.Error("EXECUTION_ERROR", $"Failed to execute: {ex.Message}");
            }
            finally
            {
                // Clean up auto-generated temp script file
                if (autoScriptPath != null)
                {
                    try { System.IO.File.Delete(autoScriptPath); } catch { }
                }
            }
        }

        /// <summary>
        /// Escapes a string for use as the value of PowerShell's -Command argument,
        /// which is already wrapped in outer double quotes by the caller.
        /// Only escapes inner double quotes.
        /// </summary>
        private string EscapeForPowerShellArg(string s)
        {
            // Escape embedded double quotes by doubling them (PowerShell convention)
            return s.Replace("\"", "\"\"");
        }

        private bool IsPowerShell(string program)
        {
            string lower = program.ToLowerInvariant();
            return lower == "powershell" || lower == "powershell.exe"
                || lower == "pwsh" || lower == "pwsh.exe"
                || lower.EndsWith("\\powershell.exe") || lower.EndsWith("\\pwsh.exe");
        }

        private bool IsCmd(string program)
        {
            string lower = program.ToLowerInvariant();
            return lower == "cmd" || lower == "cmd.exe" || lower.EndsWith("\\cmd.exe");
        }
    }
}