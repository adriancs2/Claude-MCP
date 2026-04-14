using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Build
{
    public class MsBuild : ITool
    {
        public string Name => "msbuild";
        public string Description => "Build .NET Framework projects (.csproj, .sln, .slnx) using MSBuild from the latest installed Visual Studio. Auto-discovers MSBuild.exe — no configuration needed. Supports Build, Rebuild, Clean, and Restore targets.";

        public ToolParamList Params => new ToolParamList()
            .String("project", "Full path to the .csproj, .sln, or .slnx file to build", required: true)
            .StringEnum("target", "MSBuild target to execute. Default: Build",
                new[] { "Build", "Rebuild", "Clean", "Restore" }, defaultValue: "Build")
            .StringEnum("configuration", "Build configuration. Default: Debug",
                new[] { "Debug", "Release" }, defaultValue: "Debug")
            .StringEnum("verbosity", "MSBuild output verbosity. Default: minimal",
                new[] { "quiet", "minimal", "normal", "detailed", "diagnostic" }, defaultValue: "minimal")
            .Int("timeout_seconds", "Maximum seconds to wait for the build. Default: 120", defaultValue: 120);

        public ToolResult Execute(JObject args)
        {
            string project = args.Value<string>("project");
            string target = args.Value<string>("target") ?? "Build";
            string configuration = args.Value<string>("configuration") ?? "Debug";
            string verbosity = args.Value<string>("verbosity") ?? "minimal";
            int timeoutSeconds = args.Value<int?>("timeout_seconds") ?? 120;

            if (string.IsNullOrEmpty(project))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'project' parameter");

            if (!System.IO.File.Exists(project))
                return ToolResult.Error("FILE_NOT_FOUND", $"Project file not found: {project}");

            // Auto-discover MSBuild
            string msbuildPath = MsBuildDiscovery.GetPath();

            // 2nd check
            if (msbuildPath == null)
                return ToolResult.Error("MSBUILD_NOT_FOUND",
                    "Auto discovery of MSBuild.exe failed. No Visual Studio installation detected under:\n" +
                    @"C:\Program Files\Microsoft Visual Studio" + "\n\n" +
                    "Please use run_command to manually locate MSBuild.exe, then set:\n" +
                    "MCP2.Services.MsBuildDiscovery.MSBuildPath = @\"your\\path\\to\\MSBuild.exe\";");

            // Build arguments
            string arguments = $"\"{project}\" /t:{target} /p:Configuration={configuration} /verbosity:{verbosity}";

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = msbuildPath,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                var stdoutSb = new StringBuilder();
                var stderrSb = new StringBuilder();

                using (var process = new Process { StartInfo = psi })
                {
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

                    bool exited = process.WaitForExit(timeoutSeconds * 1000);
                    if (exited)
                        process.WaitForExit(); // flush async readers

                    if (!exited)
                    {
                        try { process.Kill(); } catch { }

                        string partialOut = stdoutSb.ToString().Trim();
                        if (partialOut.Length > 20000)
                            partialOut = partialOut.Substring(0, 20000) + "\n... [TRUNCATED]";

                        return ToolResult.Error("TIMEOUT",
                            $"Build did not complete within {timeoutSeconds} seconds.\n--- Partial Output ---\n{partialOut}");
                    }

                    int exitCode = process.ExitCode;
                    string stdout = stdoutSb.ToString().Trim();
                    string stderr = stderrSb.ToString().Trim();

                    var resultSb = new StringBuilder();

                    if (exitCode == 0)
                    {
                        resultSb.AppendLine($"Build succeeded. ({target} | {configuration})");
                    }
                    else
                    {
                        resultSb.AppendLine($"Build FAILED. Exit code: {exitCode}");
                    }

                    if (!string.IsNullOrEmpty(stdout))
                    {
                        // Truncate if very large
                        if (stdout.Length > 50000)
                            stdout = stdout.Substring(0, 50000) + "\n... [TRUNCATED at 50KB]";

                        resultSb.AppendLine("--- Output ---");
                        resultSb.AppendLine(stdout);
                    }

                    if (!string.IsNullOrEmpty(stderr))
                    {
                        if (stderr.Length > 20000)
                            stderr = stderr.Substring(0, 20000) + "\n... [TRUNCATED]";

                        resultSb.AppendLine("--- Errors ---");
                        resultSb.AppendLine(stderr);
                    }

                    if (exitCode != 0)
                        return ToolResult.Error("BUILD_FAILED", resultSb.ToString());

                    return ToolResult.Success(resultSb.ToString());
                }
            }
            catch (Exception ex)
            {
                return ToolResult.Error("EXECUTION_ERROR", $"Failed to execute MSBuild: {ex.Message}");
            }
        }
    }
}
