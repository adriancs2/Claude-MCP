using System.Text;
using MCP2.Core;
using MCP2.Services;
using MySqlConnector;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class MySqlTest : ITool
    {
        public string Name => "mysql_test";
        public string Description => "Test the MySQL connection using the connection string from mysql_constr.txt.";

        public ToolParamList Params => new ToolParamList();

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            try
            {
                using (var connection = new MySqlConnection(connectionString))
                {
                    connection.Open();

                    string serverVersion = connection.ServerVersion;
                    string database = connection.Database;

                    var sb = new StringBuilder();
                    sb.AppendLine("✓ Connection successful!");
                    sb.AppendLine();
                    sb.AppendLine(string.Format("Server version: {0}", serverVersion));
                    sb.AppendLine(string.Format("Database: {0}", string.IsNullOrEmpty(database) ? "(none specified)" : database));
                    sb.AppendLine(string.Format("Connection state: {0}", connection.State));

                    return ToolResult.Success(sb.ToString());
                }
            }
            catch (MySqlException ex)
            {
                return ToolResult.Error("MYSQL_ERROR", string.Format("Connection failed - MySQL Error ({0}): {1}", ex.Number, ex.Message));
            }
            catch (System.Exception ex)
            {
                return ToolResult.Error("ERROR", "Connection failed: " + ex.Message);
            }
        }
    }
}
