using System.Collections.Generic;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Sqlite
{
    /// <summary>
    /// Returns schema info (columns, indexes, CREATE statement) for one or more
    /// tables in a SQLite database. Pass ["*"] or omit 'tables' for all tables.
    /// </summary>
    public class SqliteSchema : ITool
    {
        public string Name => "sqlite_schema";

        public string Description =>
            "Inspect the schema of a SQLite database: column definitions, indexes, and CREATE statements. " +
            "Pass 'tables' as an array of table names, or [\"*\"] / omit it for all tables. " +
            "Output format: ascii, json, csv, or markdown.";

        public ToolParamList Params => new ToolParamList()
            .String("database", "Full path to the SQLite database file", required: true)
            .Array("tables", "Array of table names, or [\"*\"] for all tables (default: all)")
            .StringEnum("format", "Output format: 'ascii' (default), 'json', 'csv', 'markdown'",
                new[] { "ascii", "json", "csv", "markdown" });

        public ToolResult Execute(JObject args)
        {
            string dbPath = args.Value<string>("database");

            string connectionString;
            var error = SqliteService.BuildConnectionString(dbPath, out connectionString, mustExist: true);
            if (error != null) return error;

            var tables = new List<string>();
            JArray arr = args.Value<JArray>("tables");
            if (arr != null)
            {
                foreach (JToken token in arr)
                {
                    string name = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(name))
                        tables.Add(name);
                }
            }

            string format = args.Value<string>("format") ?? "ascii";

            return SqliteService.GetSchema(connectionString, tables, format);
        }
    }
}
