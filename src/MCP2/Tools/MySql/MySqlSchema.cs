using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class MySqlSchema : ITool
    {
        public string Name => "mysql_schema";
        public string Description => "Get database schema information: list databases, tables, or table structure. Connection string is read from mysql_constr.txt.";

        public ToolParamList Params => new ToolParamList()
            .StringEnum("info_type", "Type of info: 'databases', 'tables', 'columns', 'create_table'",
                new[] { "databases", "tables", "columns", "create_table" }, required: true)
            .String("database", "Database name (required for 'tables', 'columns', 'create_table')")
            .String("table", "Table name (required for 'columns', 'create_table')");

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            string infoType = args.Value<string>("info_type")?.ToLowerInvariant();
            string database = args.Value<string>("database");
            string table = args.Value<string>("table");

            string query;

            switch (infoType)
            {
                case "databases":
                    query = "SHOW DATABASES";
                    break;

                case "tables":
                    if (string.IsNullOrEmpty(database))
                        return ToolResult.Error("MISSING_PARAM", "Database name is required for 'tables' info type");
                    query = string.Format("SHOW TABLES FROM `{0}`", MySqlService.EscapeIdentifier(database));
                    break;

                case "columns":
                    if (string.IsNullOrEmpty(database) || string.IsNullOrEmpty(table))
                        return ToolResult.Error("MISSING_PARAM", "Database and table names are required for 'columns' info type");
                    query = string.Format("SHOW COLUMNS FROM `{0}`.`{1}`",
                        MySqlService.EscapeIdentifier(database), MySqlService.EscapeIdentifier(table));
                    break;

                case "create_table":
                    if (string.IsNullOrEmpty(database) || string.IsNullOrEmpty(table))
                        return ToolResult.Error("MISSING_PARAM", "Database and table names are required for 'create_table' info type");
                    query = string.Format("SHOW CREATE TABLE `{0}`.`{1}`",
                        MySqlService.EscapeIdentifier(database), MySqlService.EscapeIdentifier(table));
                    break;

                default:
                    return ToolResult.Error("INVALID_PARAM", "Invalid info_type. Use: 'databases', 'tables', 'columns', or 'create_table'");
            }

            return MySqlService.ExecuteQuery(connectionString, query, 1000, "table");
        }
    }
}
