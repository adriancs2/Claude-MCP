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
        public static Dictionary<string, SshProfile> SshProfiles { get; private set; }

        /// <summary>
        /// True if the config file was auto-generated with default values and has not been customized yet.
        /// </summary>
        public static bool UsingDefaults { get; private set; }

        /// <summary>
        /// Full path to the config file
        /// </summary>
        public static string ConfigFilePath { get; private set; }

        private const string DefaultConfigJson = @"{
  ""mysql_connection_string"": ""Server=localhost;User=root;Password=1234;convertzerodatetime=true;treattinyasboolean=true;"",
  ""gc_memory_threshold_mb"": 150,
  ""debug_logging"": false,
  ""backup_directory"": null,
  ""ssh_profiles"": {
    ""myserver"": {
      ""host"": ""192.168.1.10"",
      ""port"": 22,
      ""username"": ""your-username"",
      ""password"": ""your-password""
    },
    ""myvps"": {
      ""host"": ""your-vps-hostname.com"",
      ""port"": 22,
      ""username"": ""root"",
      ""private_key_path"": ""C:\\Users\\YourName\\.ssh\\id_rsa"",
      ""passphrase"": """"
    }
  }
}";

        /// <summary>
        /// Ensures the config file exists. If not, writes the default sample config.
        /// </summary>
        public static void EnsureConfigExists()
        {
            ConfigFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp-config.json");

            if (!File.Exists(ConfigFilePath))
            {
                File.WriteAllText(ConfigFilePath, DefaultConfigJson, System.Text.Encoding.UTF8);
                UsingDefaults = true;
            }
        }

        /// <summary>
        /// Load configuration from mcp-config.json
        /// </summary>
        public static void Load()
        {
            if (ConfigFilePath == null)
            {
                EnsureConfigExists();
            }

            string json = File.ReadAllText(ConfigFilePath);
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

            // Load SSH profiles
            SshProfiles = new Dictionary<string, SshProfile>(StringComparer.OrdinalIgnoreCase);
            var sshNode = config["ssh_profiles"] as JObject;
            if (sshNode != null)
            {
                foreach (var prop in sshNode.Properties())
                {
                    var profile = prop.Value.ToObject<SshProfile>();
                    if (profile != null)
                    {
                        profile.Name = prop.Name;
                        SshProfiles[prop.Name] = profile;
                    }
                }
            }

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

    /// <summary>
    /// SSH connection profile loaded from mcp-config.json
    /// </summary>
    public class SshProfile
    {
        /// <summary>Profile name (set from the JSON property key)</summary>
        [JsonIgnore]
        public string Name { get; set; }

        [JsonProperty("host")]
        public string Host { get; set; }

        [JsonProperty("port")]
        public int Port { get; set; } = 22;

        [JsonProperty("username")]
        public string Username { get; set; }

        [JsonProperty("password")]
        public string Password { get; set; }

        [JsonProperty("private_key_path")]
        public string PrivateKeyPath { get; set; }

        [JsonProperty("passphrase")]
        public string Passphrase { get; set; }
    }
}
