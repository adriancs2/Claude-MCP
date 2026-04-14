using System;
using System.IO;
using System.Linq;

namespace MCP2.Services
{
    /// <summary>
    /// Auto-discovers MSBuild.exe from installed Visual Studio versions.
    /// Scans "C:\Program Files\Microsoft Visual Studio\{version}\..." 
    /// picks the latest version, and caches the result for the process lifetime.
    /// </summary>
    public static class MsBuildDiscovery
    {
        public static string MSBuildPath = null;

        private const string VS_BASE = @"C:\Program Files\Microsoft Visual Studio";

        // Visual Studio editions in preference order
        private static readonly string[] Editions = { "Community", "Professional", "Enterprise" };

        /// <summary>
        /// Returns the full path to MSBuild.exe, or null if not found.
        /// Auto-discovers on first call, then returns the cached result.
        /// </summary>
        public static string GetPath()
        {
            // first check
            if (MSBuildPath == null)
            {
                MSBuildPath = DiscoverMSBuildPath();
            }

            return MSBuildPath;
        }

        private static string DiscoverMSBuildPath()
        {
            if (!Directory.Exists(VS_BASE))
                return null;

            // Get version folders (e.g. "17", "18", "19") sorted descending (latest first)
            var versionDirs = Directory.GetDirectories(VS_BASE)
                .Select(d => new { Path = d, Name = Path.GetFileName(d) })
                .Where(d => d.Name.All(char.IsDigit) && d.Name.Length > 0)
                .OrderByDescending(d => int.Parse(d.Name))
                .ToList();

            // For each version (latest first), check each edition
            foreach (var ver in versionDirs)
            {
                foreach (var edition in Editions)
                {
                    string candidate = Path.Combine(ver.Path, edition, "MSBuild", "Current", "Bin", "MSBuild.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            return null;
        }
    }
}
