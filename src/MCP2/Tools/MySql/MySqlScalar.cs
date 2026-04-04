using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class MySqlScalar : ITool
    {
        public string Name => "mysql_scalar";
        public string Description => "Execute a MySQL query that returns a single value (e.g., COUNT(*), MAX(), etc.). Connection string is read from mysql_constr.txt.";

        public ToolParamList Params => new ToolParamList()
            .String("query", "SQL query to execute", required: true);

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            string query = args.Value<string>("query");
            if (string.IsNullOrEmpty(query))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'query' parameter");

            return MySqlService.ExecuteScalar(connectionString, query);
        }
    }
}
