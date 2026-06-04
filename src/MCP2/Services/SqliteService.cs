using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Text;
using MCP2.Core;

namespace MCP2.Services
{
    /// <summary>
    /// Shared SQLite service: connection management, multi-statement execution
    /// (atomic by default), schema introspection, recursive database discovery,
    /// and result formatting (ascii / json / csv / markdown).
    ///
    /// Targets Claude Code as the primary consumer: direct physical-path access
    /// (no sandbox), read+write combined in a single tool, row caps by default.
    /// </summary>
    public static class SqliteService
    {
        public const int DefaultMaxRows = 50;
        public const int HardMaxRows = 100000;

        // ═══════════════════════════════════════════════════════════════
        // CONNECTION
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a connection string for an existing database file.
        /// Returns an error ToolResult if the file is missing (null on success).
        /// </summary>
        public static ToolResult BuildConnectionString(string dbPath, out string connectionString, bool mustExist = true)
        {
            connectionString = null;

            if (string.IsNullOrWhiteSpace(dbPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'database' path parameter.");

            if (mustExist && !File.Exists(dbPath))
                return ToolResult.Error("DB_NOT_FOUND",
                    string.Format("SQLite database not found: {0}", dbPath));

            // FailIfMissing keeps us from silently creating an empty DB on a typo'd path.
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = dbPath,
                FailIfMissing = mustExist,
                ForeignKeys = true,
                BusyTimeout = 5000
            };
            connectionString = builder.ToString();
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // MULTI-STATEMENT EXECUTION (atomic)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Executes one or more SQL statements inside a single transaction.
        /// Statements that return rows (SELECT / PRAGMA reads) are formatted as
        /// result sets; others report affected-row counts. Any failure rolls back
        /// the whole batch and returns an error naming the offending statement.
        /// </summary>
        public static ToolResult ExecuteStatements(string connectionString, List<string> statements, int maxRows, string format)
        {
            if (statements == null || statements.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "No SQL statements provided.");

            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();
                    using (var transaction = connection.BeginTransaction())
                    {
                        var output = new StringBuilder();
                        bool multi = statements.Count > 1;

                        for (int s = 0; s < statements.Count; s++)
                        {
                            string sql = statements[s];
                            if (string.IsNullOrWhiteSpace(sql)) continue;

                            if (multi)
                            {
                                output.AppendLine(string.Format("─── Statement {0}/{1} ───", s + 1, statements.Count));
                            }

                            try
                            {
                                using (var command = new SQLiteCommand(sql, connection, transaction))
                                {
                                    command.CommandTimeout = 120;
                                    AppendStatementResult(command, output, maxRows, format);
                                }
                            }
                            catch (SQLiteException ex)
                            {
                                transaction.Rollback();
                                return ToolResult.Error("SQLITE_ERROR",
                                    string.Format("Statement {0} of {1} failed (transaction rolled back):\n{2}\n\nSQL: {3}",
                                        s + 1, statements.Count, ex.Message, Truncate(sql, 500)));
                            }

                            if (multi) output.AppendLine();
                        }

                        transaction.Commit();
                        return ToolResult.Success(output.ToString());
                    }
                }
            }
            catch (SQLiteException ex)
            {
                return ToolResult.Error("SQLITE_ERROR", ex.Message);
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        private static void AppendStatementResult(SQLiteCommand command, StringBuilder output, int maxRows, string format)
        {
            using (var reader = command.ExecuteReader())
            {
                if (reader.FieldCount == 0)
                {
                    // Non-row statement (INSERT/UPDATE/DELETE/DDL).
                    output.AppendLine(string.Format("OK. {0} row(s) affected.", reader.RecordsAffected));
                    return;
                }

                int columnCount = reader.FieldCount;
                string[] columnNames = new string[columnCount];
                for (int i = 0; i < columnCount; i++)
                    columnNames[i] = reader.GetName(i);

                bool isJson = string.Equals(format, "json", System.StringComparison.OrdinalIgnoreCase);

                // For text formats we keep stringified cells; for JSON we keep the
                // raw typed values so numbers/NULL serialize naturally.
                var rows = new List<string[]>();
                var typedRows = isJson ? new List<object[]>() : null;
                int rowCount = 0;
                bool truncated = false;

                while (reader.Read())
                {
                    if (rowCount >= maxRows)
                    {
                        truncated = true;
                        break;
                    }

                    if (isJson)
                    {
                        object[] trow = new object[columnCount];
                        for (int i = 0; i < columnCount; i++)
                            trow[i] = CellToJson(reader, i);
                        typedRows.Add(trow);
                    }
                    else
                    {
                        string[] row = new string[columnCount];
                        for (int i = 0; i < columnCount; i++)
                            row[i] = CellToString(reader, i);
                        rows.Add(row);
                    }
                    rowCount++;
                }

                output.Append(isJson
                    ? FormatAsJsonTyped(columnNames, typedRows)
                    : FormatRows(columnNames, rows, format));
                output.AppendLine();
                if (truncated)
                    output.AppendLine(string.Format("[Results truncated at {0} rows — pass a larger max_rows to see more]", maxRows));
                output.AppendLine(string.Format("Total: {0} row(s).", rowCount));
            }
        }

        private static string CellToString(SQLiteDataReader reader, int i)
        {
            if (reader.IsDBNull(i)) return "NULL";
            object value = reader.GetValue(i);
            if (value is byte[] bytes)
                return string.Format("[BLOB: {0} bytes]", bytes.Length);
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            return value.ToString();
        }

        /// <summary>
        /// Converts a cell to a value suitable for typed JSON: real null for
        /// DB NULL, numbers/booleans kept as-is, BLOBs and dates as descriptive
        /// strings. SQLite's dynamic typing returns Int64/Double/String/byte[].
        /// </summary>
        private static object CellToJson(SQLiteDataReader reader, int i)
        {
            if (reader.IsDBNull(i)) return null;
            object value = reader.GetValue(i);
            if (value is byte[] bytes)
                return string.Format("[BLOB: {0} bytes]", bytes.Length);
            if (value is DateTime dt)
                return dt.ToString("yyyy-MM-dd HH:mm:ss");
            // long, double, bool, string pass through and serialize natively.
            return value;
        }

        // ═══════════════════════════════════════════════════════════════
        // SCHEMA
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns schema info for the requested tables (or all tables when
        /// ["*"] is passed): column definitions, indexes, and CREATE statement.
        /// </summary>
        public static ToolResult GetSchema(string connectionString, List<string> tables, string format)
        {
            try
            {
                using (var connection = new SQLiteConnection(connectionString))
                {
                    connection.Open();

                    bool wantAll = tables == null || tables.Count == 0 ||
                                   (tables.Count == 1 && tables[0] == "*");

                    var resolved = new List<string>();
                    if (wantAll)
                    {
                        using (var cmd = new SQLiteCommand(
                            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%' ORDER BY name", connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                resolved.Add(reader.GetString(0));
                        }
                    }
                    else
                    {
                        resolved.AddRange(tables);
                    }

                    if (resolved.Count == 0)
                        return ToolResult.Success("No tables found in database.");

                    var output = new StringBuilder();
                    for (int t = 0; t < resolved.Count; t++)
                    {
                        string table = resolved[t];
                        output.AppendLine("==============================================");
                        output.AppendLine(string.Format("Table {0}/{1}: {2}", t + 1, resolved.Count, table));
                        output.AppendLine("==============================================");

                        // Columns via PRAGMA table_info
                        var colCols = new[] { "cid", "name", "type", "notnull", "dflt_value", "pk" };
                        var colRows = new List<string[]>();
                        using (var cmd = new SQLiteCommand(string.Format("PRAGMA table_info(\"{0}\")", table.Replace("\"", "\"\"")), connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var r = new string[colCols.Length];
                                for (int i = 0; i < colCols.Length; i++)
                                    r[i] = CellToString(reader, i);
                                colRows.Add(r);
                            }
                        }

                        if (colRows.Count == 0)
                        {
                            output.AppendLine("(table not found or has no columns)");
                            output.AppendLine();
                            continue;
                        }

                        output.AppendLine("Columns:");
                        output.AppendLine(FormatRows(colCols, colRows, format));
                        output.AppendLine();

                        // Indexes
                        var idxCols = new[] { "seq", "name", "unique", "origin", "partial" };
                        var idxRows = new List<string[]>();
                        using (var cmd = new SQLiteCommand(string.Format("PRAGMA index_list(\"{0}\")", table.Replace("\"", "\"\"")), connection))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                var r = new string[idxCols.Length];
                                for (int i = 0; i < idxCols.Length && i < reader.FieldCount; i++)
                                    r[i] = CellToString(reader, i);
                                idxRows.Add(r);
                            }
                        }
                        if (idxRows.Count > 0)
                        {
                            output.AppendLine("Indexes:");
                            output.AppendLine(FormatRows(idxCols, idxRows, format));
                            output.AppendLine();
                        }

                        // CREATE statement
                        using (var cmd = new SQLiteCommand(
                            "SELECT sql FROM sqlite_master WHERE type='table' AND name=@n", connection))
                        {
                            cmd.Parameters.AddWithValue("@n", table);
                            object sql = cmd.ExecuteScalar();
                            if (sql != null && sql != DBNull.Value)
                            {
                                output.AppendLine("CREATE statement:");
                                output.AppendLine(sql.ToString() + ";");
                                output.AppendLine();
                            }
                        }
                    }

                    output.AppendLine("==============================================");
                    output.AppendLine(string.Format("Total: {0} table(s).", resolved.Count));
                    return ToolResult.Success(output.ToString());
                }
            }
            catch (SQLiteException ex)
            {
                return ToolResult.Error("SQLITE_ERROR", ex.Message);
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // DISCOVERY
        // ═══════════════════════════════════════════════════════════════

        private static readonly string[] DefaultExtensions = { ".db", ".sqlite", ".sqlite3", ".db3" };

        /// <summary>
        /// Recursively finds SQLite database files under a root directory.
        /// </summary>
        public static ToolResult ListDatabases(string root, bool recursive, string[] extensions)
        {
            if (string.IsNullOrWhiteSpace(root))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'directory' parameter.");
            if (!Directory.Exists(root))
                return ToolResult.Error("DIR_NOT_FOUND", string.Format("Directory not found: {0}", root));

            string[] exts = (extensions != null && extensions.Length > 0) ? extensions : DefaultExtensions;
            var extSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in exts)
                extSet.Add(e.StartsWith(".") ? e : "." + e);

            var found = new List<string[]>();
            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;

            try
            {
                EnumerateFiles(root, option, extSet, found);
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }

            if (found.Count == 0)
                return ToolResult.Success(string.Format("No SQLite files found under {0} (extensions: {1}).",
                    root, string.Join(", ", exts)));

            var cols = new[] { "path", "size_bytes", "modified_utc" };
            var sb = new StringBuilder();
            sb.AppendLine(FormatRows(cols, found, "ascii"));
            sb.AppendLine();
            sb.AppendLine(string.Format("Total: {0} database file(s).", found.Count));
            return ToolResult.Success(sb.ToString());
        }

        private static void EnumerateFiles(string dir, SearchOption option, HashSet<string> extSet, List<string[]> found)
        {
            string[] files;
            try { files = Directory.GetFiles(dir); }
            catch (UnauthorizedAccessException) { return; }

            foreach (var file in files)
            {
                if (extSet.Contains(Path.GetExtension(file)))
                {
                    var info = new FileInfo(file);
                    found.Add(new[]
                    {
                        file,
                        info.Length.ToString(),
                        info.LastWriteTimeUtc.ToString("yyyy-MM-dd HH:mm:ss")
                    });
                }
            }

            if (option == SearchOption.AllDirectories)
            {
                string[] subdirs;
                try { subdirs = Directory.GetDirectories(dir); }
                catch (UnauthorizedAccessException) { return; }
                foreach (var sub in subdirs)
                    EnumerateFiles(sub, option, extSet, found);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // CREATE
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Explicitly creates a new SQLite database file. Refuses to overwrite an
        /// existing file unless overwrite is set.
        /// </summary>
        public static ToolResult CreateDatabase(string dbPath, bool overwrite)
        {
            if (string.IsNullOrWhiteSpace(dbPath))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'database' path parameter.");

            try
            {
                if (File.Exists(dbPath))
                {
                    if (!overwrite)
                        return ToolResult.Error("DB_EXISTS",
                            string.Format("Database already exists: {0}. Pass overwrite=true to replace it.", dbPath));
                    File.Delete(dbPath);
                }

                string dir = Path.GetDirectoryName(dbPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                SQLiteConnection.CreateFile(dbPath);

                // Touch it so the file is a valid (empty) SQLite DB on disk.
                var builder = new SQLiteConnectionStringBuilder { DataSource = dbPath };
                using (var connection = new SQLiteConnection(builder.ToString()))
                {
                    connection.Open();
                    using (var cmd = new SQLiteCommand("PRAGMA user_version;", connection))
                        cmd.ExecuteNonQuery();
                }

                return ToolResult.Success(string.Format("Created SQLite database: {0}", dbPath));
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // FORMATTING
        // ═══════════════════════════════════════════════════════════════

        public static string FormatRows(string[] columns, List<string[]> rows, string format)
        {
            switch ((format ?? "ascii").ToLowerInvariant())
            {
                case "json": return FormatAsJson(columns, rows);
                case "csv": return FormatAsCsv(columns, rows);
                case "markdown":
                case "md": return FormatAsMarkdown(columns, rows);
                case "ascii":
                case "table":
                default: return FormatAsAscii(columns, rows);
            }
        }

        private static string FormatAsAscii(string[] columns, List<string[]> rows)
        {
            int[] widths = ComputeWidths(columns, rows);
            var sb = new StringBuilder();

            var sep = new StringBuilder("+");
            for (int i = 0; i < columns.Length; i++)
                sep.Append(new string('-', widths[i] + 2)).Append("+");

            sb.AppendLine(sep.ToString());

            var header = new StringBuilder("|");
            for (int i = 0; i < columns.Length; i++)
                header.Append(" ").Append(columns[i].PadRight(widths[i])).Append(" |");
            sb.AppendLine(header.ToString());
            sb.AppendLine(sep.ToString());

            foreach (var row in rows)
            {
                var line = new StringBuilder("|");
                for (int i = 0; i < columns.Length; i++)
                {
                    string cell = i < row.Length ? (row[i] ?? "NULL") : "";
                    line.Append(" ").Append(cell.PadRight(widths[i])).Append(" |");
                }
                sb.AppendLine(line.ToString());
            }
            sb.AppendLine(sep.ToString());
            return sb.ToString();
        }

        private static string FormatAsMarkdown(string[] columns, List<string[]> rows)
        {
            int[] widths = ComputeWidths(columns, rows);
            var sb = new StringBuilder();

            var header = new StringBuilder("|");
            var divider = new StringBuilder("|");
            for (int i = 0; i < columns.Length; i++)
            {
                header.Append(" ").Append(columns[i].PadRight(widths[i])).Append(" |");
                divider.Append(" ").Append(new string('-', widths[i])).Append(" |");
            }
            sb.AppendLine(header.ToString());
            sb.AppendLine(divider.ToString());

            foreach (var row in rows)
            {
                var line = new StringBuilder("|");
                for (int i = 0; i < columns.Length; i++)
                {
                    string cell = i < row.Length ? (row[i] ?? "NULL") : "";
                    cell = cell.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
                    line.Append(" ").Append(cell.PadRight(widths[i])).Append(" |");
                }
                sb.AppendLine(line.ToString());
            }
            return sb.ToString();
        }

        private static string FormatAsCsv(string[] columns, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CsvLine(columns));
            foreach (var row in rows)
                sb.AppendLine(CsvLine(row));
            return sb.ToString();
        }

        private static string CsvLine(string[] values)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < values.Length; i++)
            {
                if (i > 0) sb.Append(",");
                string v = values[i] ?? "";
                if (v.IndexOf(',') >= 0 || v.IndexOf('"') >= 0 || v.IndexOf('\n') >= 0 || v.IndexOf('\r') >= 0)
                    v = "\"" + v.Replace("\"", "\"\"") + "\"";
                sb.Append(v);
            }
            return sb.ToString();
        }

        private static string FormatAsJsonTyped(string[] columns, List<object[]> rows)
        {
            var list = new List<Dictionary<string, object>>();
            foreach (var row in rows)
            {
                var obj = new Dictionary<string, object>();
                for (int i = 0; i < columns.Length; i++)
                    obj[columns[i]] = i < row.Length ? row[i] : null;
                list.Add(obj);
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(list, Newtonsoft.Json.Formatting.Indented);
        }

        private static string FormatAsJson(string[] columns, List<string[]> rows)
        {
            var list = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                var obj = new Dictionary<string, string>();
                for (int i = 0; i < columns.Length; i++)
                    obj[columns[i]] = i < row.Length ? row[i] : null;
                list.Add(obj);
            }
            return Newtonsoft.Json.JsonConvert.SerializeObject(list, Newtonsoft.Json.Formatting.Indented);
        }

        private static int[] ComputeWidths(string[] columns, List<string[]> rows)
        {
            int[] widths = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
                widths[i] = columns[i].Length;
            foreach (var row in rows)
                for (int i = 0; i < columns.Length; i++)
                {
                    int len = (i < row.Length && row[i] != null) ? row[i].Length : 4;
                    if (len > widths[i]) widths[i] = len;
                }
            return widths;
        }

        private static string Truncate(string s, int max)
        {
            if (s == null) return "";
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }
    }
}
