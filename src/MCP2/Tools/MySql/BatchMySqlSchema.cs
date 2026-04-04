using System.Collections.Generic;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using MySqlConnector;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class BatchMySqlSchema : ITool
    {
        public string Name => "batch_mysql_schema";
        public string Description => "Get database schema information for multiple tables in a single operation. Returns table structures, columns, or CREATE statements.";

        public ToolParamList Params => new ToolParamList()
            .String("database", "Database name", required: true)
            .Array("tables", "Array of table names (or [\"*\"] for all tables)", required: true)
            .StringEnum("info_type", "Type of info: 'columns' (default), 'create_table', or 'both'",
                new[] { "columns", "create_table", "both" })
            .StringEnum("format", "Output format: 'table' (default) or 'json'",
                new[] { "table", "json" });

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            string database = args.Value<string>("database");
            if (string.IsNullOrEmpty(database))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'database' parameter");

            JArray tablesArray = args.Value<JArray>("tables");
            if (tablesArray == null || tablesArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'tables' parameter");

            string infoType = args.Value<string>("info_type") ?? "columns";
            string format = args.Value<string>("format") ?? "table";

            try
            {
                var tables = new List<string>();
                foreach (JToken tableToken in tablesArray)
                    tables.Add(tableToken.Value<string>());

                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    // If "*" specified, get all tables
                    if (tables.Count == 1 && tables[0] == "*")
                    {
                        tables.Clear();
                        using (var cmd = new MySqlCommand(string.Format("SHOW TABLES FROM `{0}`", database), conn))
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                                tables.Add(reader.GetString(0));
                        }
                    }

                    var output = new StringBuilder();

                    if (format == "json")
                        output.AppendLine("[");

                    for (int i = 0; i < tables.Count; i++)
                    {
                        string table = tables[i];

                        if (format == "table")
                        {
                            output.AppendLine("==============================================");
                            output.AppendLine(string.Format("Table {0}/{1}: {2}.{3}", i + 1, tables.Count, database, table));
                            output.AppendLine("==============================================");
                            output.AppendLine();
                        }

                        // Get columns info
                        if (infoType == "columns" || infoType == "both")
                        {
                            string query = string.Format("DESCRIBE `{0}`.`{1}`", database, table);
                            using (var cmd = new MySqlCommand(query, conn))
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (format == "table")
                                {
                                    output.AppendLine("Columns:");
                                    output.AppendLine(string.Format("{0,-20} {1,-15} {2,-5} {3,-5} {4,-10}",
                                        "Column", "Type", "Null", "Key", "Default"));
                                    output.AppendLine(new string('-', 60));

                                    while (reader.Read())
                                    {
                                        output.AppendLine(string.Format("{0,-20} {1,-15} {2,-5} {3,-5} {4,-10}",
                                            reader["Field"], reader["Type"], reader["Null"],
                                            reader["Key"], reader["Default"] ?? "NULL"));
                                    }
                                    output.AppendLine();
                                }
                                else
                                {
                                    var columns = new List<string>();
                                    while (reader.Read())
                                    {
                                        columns.Add(string.Format("{{\"column\":\"{0}\",\"type\":\"{1}\",\"null\":\"{2}\",\"key\":\"{3}\",\"default\":\"{4}\"}}",
                                            reader["Field"], reader["Type"], reader["Null"],
                                            reader["Key"], reader["Default"] ?? "NULL"));
                                    }
                                    output.AppendLine(string.Format("  {{\"table\":\"{0}\",\"columns\":[{1}]}}{2}",
                                        table, string.Join(",", columns.ToArray()), i < tables.Count - 1 ? "," : ""));
                                }
                            }
                        }

                        // Get CREATE TABLE statement
                        if (infoType == "create_table" || infoType == "both")
                        {
                            string query = string.Format("SHOW CREATE TABLE `{0}`.`{1}`", database, table);
                            using (var cmd = new MySqlCommand(query, conn))
                            using (var reader = cmd.ExecuteReader())
                            {
                                if (reader.Read())
                                {
                                    if (format == "table")
                                    {
                                        if (infoType == "both")
                                            output.AppendLine("CREATE TABLE Statement:");
                                        output.AppendLine(reader.GetString(1));
                                        output.AppendLine();
                                    }
                                }
                            }
                        }

                        if (format == "table")
                            output.AppendLine();
                    }

                    if (format == "json")
                    {
                        output.AppendLine("]");
                    }
                    else
                    {
                        output.AppendLine("==============================================");
                        output.AppendLine(string.Format("Total: {0} table(s)", tables.Count));
                        output.AppendLine("==============================================");
                    }

                    return ToolResult.Success(output.ToString());
                }
            }
            catch (MySqlException ex)
            {
                return ToolResult.Error("MYSQL_ERROR", ex.Message);
            }
            catch (System.Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }
    }
}
