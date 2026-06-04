using System.Collections.Generic;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Sqlite
{
    /// <summary>
    /// Combined read+write SQLite tool. Accepts a single SQL string or an array
    /// of statements executed atomically in one transaction. SELECT/PRAGMA reads
    /// return formatted result sets; writes report affected rows.
    /// </summary>
    public class SqliteQuery : ITool
    {
        public string Name => "sqlite_query";

        public string Description =>
            "Run SQL against a SQLite database file (read AND write). Accepts a single statement via 'sql' " +
            "or multiple statements via 'statements' (array) which run atomically in one transaction — any " +
            "failure rolls back the whole batch. SELECT/PRAGMA return formatted rows (default 50, override with " +
            "max_rows); INSERT/UPDATE/DELETE/DDL report affected rows. Output format: ascii, json, csv, or markdown.";

        public ToolParamList Params => new ToolParamList()
            .String("database", "Full path to the SQLite database file", required: true)
            .String("sql", "A single SQL statement (use this OR 'statements')")
            .Array("statements", "Array of SQL statements run atomically in one transaction (use this OR 'sql')")
            .Int("max_rows", "Max rows to return per result set (default: 50)")
            .StringEnum("format", "Output format: 'ascii' (default), 'json', 'csv', 'markdown'",
                new[] { "ascii", "json", "csv", "markdown" });

        public ToolResult Execute(JObject args)
        {
            string dbPath = args.Value<string>("database");

            string connectionString;
            var error = SqliteService.BuildConnectionString(dbPath, out connectionString, mustExist: true);
            if (error != null) return error;

            var statements = new List<string>();
            JArray arr = args.Value<JArray>("statements");
            if (arr != null && arr.Count > 0)
            {
                foreach (JToken token in arr)
                {
                    string stmt = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(stmt))
                        statements.Add(stmt);
                }
            }
            else
            {
                string sql = args.Value<string>("sql");
                if (!string.IsNullOrWhiteSpace(sql))
                    statements.Add(sql);
            }

            if (statements.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Provide 'sql' (string) or 'statements' (array).");

            int maxRows = args.Value<int?>("max_rows") ?? SqliteService.DefaultMaxRows;
            if (maxRows < 1) maxRows = 1;
            if (maxRows > SqliteService.HardMaxRows) maxRows = SqliteService.HardMaxRows;

            string format = args.Value<string>("format") ?? "ascii";

            return SqliteService.ExecuteStatements(connectionString, statements, maxRows, format);
        }
    }
}
