using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class MySqlSelect : ITool
    {
        public string Name => "mysql_select";
        public string Description => "Execute a MySQL query that returns a result set (SELECT, SHOW, DESCRIBE, EXPLAIN). Returns data in a formatted table. Connection string is read from mysql_constr.txt.";

        public ToolParamList Params => new ToolParamList()
            .String("query", "SQL query to execute", required: true)
            .Int("max_rows", "Maximum rows to return (default: 1000, max: 10000)")
            .StringEnum("format", "Output format: 'table' (default), 'csv', 'json'",
                new[] { "table", "csv", "json" });

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            string query = args.Value<string>("query");
            if (string.IsNullOrEmpty(query))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'query' parameter");

            int maxRows = args.Value<int?>("max_rows") ?? 1000;
            if (maxRows > 10000) maxRows = 10000;
            if (maxRows < 1) maxRows = 1;

            string format = args.Value<string>("format") ?? "table";

            return MySqlService.ExecuteQuery(connectionString, query, maxRows, format);
        }
    }
}
