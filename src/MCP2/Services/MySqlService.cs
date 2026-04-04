using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using MCP2.Core;
using MySqlConnector;
using Newtonsoft.Json.Linq;

namespace MCP2.Services
{
    /// <summary>
    /// Shared MySQL service providing connection management, query execution, and formatting.
    /// Connection string is read from mcp-config.json via McpConfig.
    /// </summary>
    public static class MySqlService
    {
        // ═══════════════════════════════════════════════════════════════
        // CONNECTION MANAGEMENT
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Gets the connection string from McpConfig (loaded from mcp-config.json).
        /// Returns null ToolResult on success, error ToolResult on failure.
        /// </summary>
        public static ToolResult GetConnectionString(out string connectionString)
        {
            connectionString = McpConfig.MySqlConnectionString;

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                return ToolResult.Error("CONFIG_NOT_FOUND",
                    "MySQL connection string not found in mcp-config.json.\n\nPlease add \"mysql_connection_string\" to your mcp-config.json, e.g.:\n\"mysql_connection_string\": \"Server=localhost;Database=mydb;User=root;Password=yourpassword;\"");
            }

            return null; // success
        }

        // ═══════════════════════════════════════════════════════════════
        // QUERY EXECUTION
        // ═══════════════════════════════════════════════════════════════

        public static ToolResult ExecuteNonQuery(string connectionString, string query)
        {
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.CommandTimeout = 60;
                        int affectedRows = command.ExecuteNonQuery();

                        var sb = new StringBuilder();
                        sb.AppendLine("Query executed successfully.");
                        sb.AppendLine(string.Format("Affected rows: {0}", affectedRows));
                        return ToolResult.Success(sb.ToString());
                    }
                }
            }
            catch (MySqlException ex)
            {
                return ToolResult.Error("MYSQL_ERROR", string.Format("MySQL Error ({0}): {1}", ex.Number, ex.Message));
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        public static ToolResult ExecuteQuery(string connectionString, string query, int maxRows, string format)
        {
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.CommandTimeout = 60;
                        using (var reader = command.ExecuteReader())
                        {
                            int columnCount = reader.FieldCount;
                            string[] columnNames = new string[columnCount];
                            for (int i = 0; i < columnCount; i++)
                                columnNames[i] = reader.GetName(i);

                            var rows = new List<string[]>();
                            int rowCount = 0;
                            bool truncated = false;

                            while (reader.Read())
                            {
                                if (rowCount >= maxRows)
                                {
                                    truncated = true;
                                    break;
                                }

                                string[] row = new string[columnCount];
                                for (int i = 0; i < columnCount; i++)
                                {
                                    if (reader.IsDBNull(i))
                                        row[i] = "NULL";
                                    else
                                    {
                                        object value = reader.GetValue(i);
                                        if (value is byte[] bytes)
                                            row[i] = string.Format("[BLOB: {0} bytes]", bytes.Length);
                                        else if (value is DateTime dt)
                                            row[i] = dt.ToString("yyyy-MM-dd HH:mm:ss");
                                        else
                                            row[i] = value.ToString();
                                    }
                                }
                                rows.Add(row);
                                rowCount++;
                            }

                            string output;
                            switch (format.ToLowerInvariant())
                            {
                                case "csv": output = FormatAsCsv(columnNames, rows); break;
                                case "json": output = FormatAsJson(columnNames, rows); break;
                                default: output = FormatAsTable(columnNames, rows); break;
                            }

                            var sb = new StringBuilder();
                            sb.Append(output);
                            if (truncated)
                            {
                                sb.AppendLine();
                                sb.AppendLine(string.Format("[Results truncated at {0} rows]", maxRows));
                            }
                            sb.AppendLine();
                            sb.AppendLine(string.Format("Total: {0} row(s) returned", rowCount));
                            return ToolResult.Success(sb.ToString());
                        }
                    }
                }
            }
            catch (MySqlException ex)
            {
                return ToolResult.Error("MYSQL_ERROR", string.Format("MySQL Error ({0}): {1}", ex.Number, ex.Message));
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        public static ToolResult ExecuteScalar(string connectionString, string query)
        {
            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();
                    using (var command = new MySqlCommand(query, connection))
                    {
                        command.CommandTimeout = 60;
                        object result = command.ExecuteScalar();

                        string value;
                        if (result == null || result == DBNull.Value)
                            value = "NULL";
                        else if (result is byte[] bytes)
                            value = string.Format("[BLOB: {0} bytes]", bytes.Length);
                        else
                            value = result.ToString();

                        return ToolResult.Success(value);
                    }
                }
            }
            catch (MySqlException ex)
            {
                return ToolResult.Error("MYSQL_ERROR", string.Format("MySQL Error ({0}): {1}", ex.Number, ex.Message));
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // FORMATTING
        // ═══════════════════════════════════════════════════════════════

        public static string FormatAsTable(string[] columns, List<string[]> rows)
        {
            int[] widths = new int[columns.Length];
            for (int i = 0; i < columns.Length; i++)
                widths[i] = columns[i].Length;

            foreach (var row in rows)
            {
                for (int i = 0; i < columns.Length; i++)
                {
                    int len = row[i] != null ? row[i].Length : 4;
                    if (len > widths[i])
                        widths[i] = len;
                }
            }

            var sb = new StringBuilder();

            // Header
            var header = new StringBuilder("| ");
            var separator = new StringBuilder("+-");
            for (int i = 0; i < columns.Length; i++)
            {
                header.Append(columns[i].PadRight(widths[i]));
                header.Append(" | ");
                separator.Append(new string('-', widths[i]));
                separator.Append("-+-");
            }

            sb.AppendLine(separator.ToString().TrimEnd('-', '+') + "+");
            sb.AppendLine(header.ToString().TrimEnd());
            sb.AppendLine(separator.ToString().TrimEnd('-', '+') + "+");

            foreach (var row in rows)
            {
                var rowSb = new StringBuilder("| ");
                for (int i = 0; i < columns.Length; i++)
                {
                    string value = row[i] ?? "NULL";
                    rowSb.Append(value.PadRight(widths[i]));
                    rowSb.Append(" | ");
                }
                sb.AppendLine(rowSb.ToString().TrimEnd());
            }

            sb.AppendLine(separator.ToString().TrimEnd('-', '+') + "+");
            return sb.ToString();
        }

        public static string FormatAsCsv(string[] columns, List<string[]> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Array.ConvertAll(columns, c => CsvEscape(c))));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", Array.ConvertAll(row, v => CsvEscape(v))));
            return sb.ToString();
        }

        public static string FormatAsJson(string[] columns, List<string[]> rows)
        {
            var array = new JArray();
            foreach (var row in rows)
            {
                var obj = new JObject();
                for (int i = 0; i < columns.Length; i++)
                    obj[columns[i]] = row[i] == "NULL" ? null : row[i];
                array.Add(obj);
            }
            return array.ToString(Newtonsoft.Json.Formatting.Indented);
        }

        /// <summary>
        /// Format a DataTable as a readable text table. Used by batch query tools.
        /// </summary>
        public static string FormatDataTable(DataTable dt, int maxRows)
        {
            if (dt.Rows.Count == 0)
                return "(No rows returned)";

            var sb = new StringBuilder();

            int[] columnWidths = new int[dt.Columns.Count];
            for (int i = 0; i < dt.Columns.Count; i++)
                columnWidths[i] = dt.Columns[i].ColumnName.Length;

            int rowsToProcess = Math.Min(dt.Rows.Count, maxRows);
            for (int i = 0; i < rowsToProcess; i++)
            {
                for (int j = 0; j < dt.Columns.Count; j++)
                {
                    string value = dt.Rows[i][j]?.ToString() ?? "NULL";
                    if (value.Length > columnWidths[j])
                        columnWidths[j] = Math.Min(value.Length, 50);
                }
            }

            // Header
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                sb.Append(dt.Columns[i].ColumnName.PadRight(columnWidths[i] + 2));
                if (i < dt.Columns.Count - 1) sb.Append("| ");
            }
            sb.AppendLine();

            // Separator
            for (int i = 0; i < dt.Columns.Count; i++)
            {
                sb.Append(new string('-', columnWidths[i] + 2));
                if (i < dt.Columns.Count - 1) sb.Append("+-");
            }
            sb.AppendLine();

            // Rows
            for (int i = 0; i < rowsToProcess; i++)
            {
                for (int j = 0; j < dt.Columns.Count; j++)
                {
                    string value = dt.Rows[i][j]?.ToString() ?? "NULL";
                    if (value.Length > 50) value = value.Substring(0, 47) + "...";
                    sb.Append(value.PadRight(columnWidths[j] + 2));
                    if (j < dt.Columns.Count - 1) sb.Append("| ");
                }
                sb.AppendLine();
            }

            sb.AppendLine();
            sb.AppendLine(string.Format("({0} row(s) returned)", dt.Rows.Count));
            return sb.ToString();
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════════

        public static string EscapeIdentifier(string identifier)
        {
            return identifier.Replace("`", "``").Replace("\\", "");
        }

        public static string CsvEscape(string value)
        {
            if (value == null) return "";
            if (value.Contains(",") || value.Contains("\"") || value.Contains("\n") || value.Contains("\r"))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        /// <summary>
        /// Replace {variable} placeholders in a query string. Used by batch queries with variables.
        /// </summary>
        public static string ReplaceVariables(string query, Dictionary<string, string> variables)
        {
            if (variables.Count == 0) return query;

            string result = query;
            foreach (var kvp in variables)
            {
                string placeholder = "{" + kvp.Key + "}";
                result = result.Replace(placeholder, kvp.Value);
            }
            return result;
        }
    }
}
