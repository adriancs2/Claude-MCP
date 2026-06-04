using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Sqlite
{
    /// <summary>
    /// Explicitly creates a new (empty) SQLite database file. Kept separate from
    /// sqlite_query so a typo'd path can't silently create a stray database.
    /// </summary>
    public class SqliteCreateDatabase : ITool
    {
        public string Name => "sqlite_create_database";

        public string Description =>
            "Create a new, empty SQLite database file at the given path. Refuses to overwrite an existing file " +
            "unless overwrite=true. Parent directories are created as needed.";

        public ToolParamList Params => new ToolParamList()
            .String("database", "Full path for the new SQLite database file", required: true)
            .Bool("overwrite", "Replace the file if it already exists (default: false)");

        public ToolResult Execute(JObject args)
        {
            string dbPath = args.Value<string>("database");
            bool overwrite = args.Value<bool?>("overwrite") ?? false;
            return SqliteService.CreateDatabase(dbPath, overwrite);
        }
    }
}
