using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class MySqlExecute : ITool
    {
        public string Name => "mysql_execute";
        public string Description => "Execute a MySQL statement that doesn't return a result set (INSERT, UPDATE, DELETE, CREATE, ALTER, DROP, etc.). Returns the number of affected rows. Connection string is read from mysql_constr.txt.";

        public ToolParamList Params => new ToolParamList()
            .String("query", "SQL statement to execute", required: true);

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            string query = args.Value<string>("query");
            if (string.IsNullOrEmpty(query))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'query' parameter");

            return MySqlService.ExecuteNonQuery(connectionString, query);
        }
    }
}
