using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MCP2.Core;

namespace MCP2.Services
{
    /// <summary>
    /// Provides automatic backup functionality for file edits.
    /// Creates timestamped backups before modifications to enable undo functionality.
    /// </summary>
    public class BackupService
    {
        private readonly string _backupDirectory;

        public BackupService()
        {
            // Read backup_directory from McpConfig
            // If null, use "./backups" relative to exe
            _backupDirectory = McpConfig.BackupDirectory ?? 
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "backups");
            
            // Ensure backup directory exists
            if (!Directory.Exists(_backupDirectory))
                Directory.CreateDirectory(_backupDirectory);
        }

        /// <summary>
        /// Creates a timestamped backup of a file
        /// </summary>
        /// <param name="filePath">Full path to the file to backup</param>
        /// <returns>Path to the created backup file</returns>
        /// <exception cref="FileNotFoundException">If source file doesn't exist</exception>
        public string CreateBackup(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Cannot backup non-existent file: {filePath}");

            string fileName = Path.GetFileName(filePath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string backupFileName = $"{fileName}.{timestamp}.bak";
            string backupPath = Path.Combine(_backupDirectory, backupFileName);

            // Handle rapid successive edits - add milliseconds if file exists
            if (File.Exists(backupPath))
            {
                timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
                backupFileName = $"{fileName}.{timestamp}.bak";
                backupPath = Path.Combine(_backupDirectory, backupFileName);
            }

            File.Copy(filePath, backupPath);
            return backupPath;
        }

        /// <summary>
        /// Gets all backups for a specific file
        /// </summary>
        /// <param name="fileName">Name of the file (without path)</param>
        /// <returns>List of backup file paths sorted by timestamp (newest first)</returns>
        public List<string> GetBackupsForFile(string fileName)
        {
            if (!Directory.Exists(_backupDirectory))
                return new List<string>();

            string searchPattern = $"{fileName}.*.bak";
            var backups = Directory.GetFiles(_backupDirectory, searchPattern)
                .OrderByDescending(f => File.GetCreationTime(f))
                .ToList();

            return backups;
        }

        /// <summary>
        /// Gets the most recent backup for a file
        /// </summary>
        /// <param name="fileName">Name of the file (without path)</param>
        /// <returns>Path to most recent backup, or null if none exist</returns>
        public string GetLatestBackup(string fileName)
        {
            var backups = GetBackupsForFile(fileName);
            return backups.FirstOrDefault();
        }

        /// <summary>
        /// Clears backups older than specified days
        /// </summary>
        /// <param name="daysToKeep">Number of days to keep backups</param>
        /// <returns>Count of deleted backups</returns>
        public int ClearOldBackups(int daysToKeep)
        {
            if (!Directory.Exists(_backupDirectory))
                return 0;

            DateTime cutoffDate = DateTime.Now.AddDays(-daysToKeep);
            int deletedCount = 0;

            foreach (var backupFile in Directory.GetFiles(_backupDirectory, "*.bak"))
            {
                if (File.GetCreationTime(backupFile) < cutoffDate)
                {
                    File.Delete(backupFile);
                    deletedCount++;
                }
            }

            return deletedCount;
        }

        /// <summary>
        /// Gets the backup directory path
        /// </summary>
        /// <returns>Full path to backup directory</returns>
        public string GetBackupDirectory()
        {
            return _backupDirectory;
        }
    }
}