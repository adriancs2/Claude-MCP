using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace MCP2.Core
{
    /// <summary>
    /// Configuration loader for MCP2
    /// </summary>
    public static class McpConfig
    {
        public static List<string> AllowedDirectories { get; private set; }
        public static string MySqlConnectionString { get; private set; }
        public static int GcMemoryThresholdMb { get; private set; }
        public static bool DebugLogging { get; private set; }
        public static bool CallerValidation { get; private set; } = true;
        public static string BackupDirectory { get; private set; }
        public static string MsBuildPath { get; private set; }
        public static string NuGetPath { get; private set; }

        /// <summary>
        /// Load configuration from mcp-config.json
        /// </summary>
        public static void Load()
        {
            string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-config.json");
            
            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException(string.Format("Configuration file not found: {0}", configPath));
            }

            string json = File.ReadAllText(configPath);
            JObject config = JObject.Parse(json);

            // Load allowed directories
            AllowedDirectories = config["allowed_directories"]?.ToObject<List<string>>() ?? new List<string>();
            
            // Normalize paths (handle forward/back slashes, trailing slashes)
            AllowedDirectories = AllowedDirectories
                .Select(d => Path.GetFullPath(d.Replace('/', '\\')))
                .ToList();

            // Load other settings
            MySqlConnectionString = config["mysql_connection_string"]?.Value<string>() ?? string.Empty;
            GcMemoryThresholdMb = config["gc_memory_threshold_mb"]?.Value<int>() ?? 150;
            DebugLogging = config["debug_logging"]?.Value<bool>() ?? false;
            CallerValidation = config["caller_validation"]?.Value<bool>() ?? true;
            BackupDirectory = config["backup_directory"]?.Value<string>();
            MsBuildPath = config["msbuild_path"]?.Value<string>();
            NuGetPath = config["nuget_path"]?.Value<string>();

            // Normalize optional paths if provided
            if (!string.IsNullOrEmpty(BackupDirectory))
            {
                BackupDirectory = Path.GetFullPath(BackupDirectory);
            }

            if (!string.IsNullOrEmpty(MsBuildPath))
            {
                MsBuildPath = Path.GetFullPath(MsBuildPath);
            }

            if (!string.IsNullOrEmpty(NuGetPath))
            {
                NuGetPath = Path.GetFullPath(NuGetPath);
            }
        }
    }
}