using System.Collections.Generic;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Sqlite
{
    /// <summary>
    /// Recursively discovers SQLite database files under a directory.
    /// </summary>
    public class SqliteListDatabases : ITool
    {
        public string Name => "sqlite_list_databases";

        public string Description =>
            "Find SQLite database files under a directory (recursive by default). Returns path, size, and " +
            "last-modified time for each. Matches .db/.sqlite/.sqlite3/.db3 by default, or pass custom 'extensions'.";

        public ToolParamList Params => new ToolParamList()
            .String("directory", "Root directory to search", required: true)
            .Bool("recursive", "Search sub-folders recursively (default: true)")
            .Array("extensions", "File extensions to match (default: .db, .sqlite, .sqlite3, .db3)");

        public ToolResult Execute(JObject args)
        {
            string root = args.Value<string>("directory");
            bool recursive = args.Value<bool?>("recursive") ?? true;

            string[] extensions = null;
            JArray arr = args.Value<JArray>("extensions");
            if (arr != null && arr.Count > 0)
            {
                var list = new List<string>();
                foreach (JToken token in arr)
                {
                    string ext = token.Value<string>();
                    if (!string.IsNullOrWhiteSpace(ext))
                        list.Add(ext);
                }
                extensions = list.ToArray();
            }

            return SqliteService.ListDatabases(root, recursive, extensions);
        }
    }
}
