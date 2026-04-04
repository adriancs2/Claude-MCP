using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using MCP2.Core;
using MCP2.Services;
using MySqlConnector;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.MySql
{
    public class BatchMySqlQueries : ITool
    {
        public string Name => "batch_mysql_queries";
        public string Description => "Execute multiple MySQL queries in a single operation. Supports SELECT, scalar queries, and mixed types. All queries run in a single transaction.";

        public ToolParamList Params => new ToolParamList()
            .Array("queries", "Array of query specifications with query, type, and optional label", required: true)
            .StringEnum("format", "Output format: 'separated' (default) or 'combined'",
                new[] { "separated", "combined" });

        public ToolResult Execute(JObject args)
        {
            string connectionString;
            var configError = MySqlService.GetConnectionString(out connectionString);
            if (configError != null) return configError;

            JArray queriesArray = args.Value<JArray>("queries");
            if (queriesArray == null || queriesArray.Count == 0)
                return ToolResult.Error("INVALID_PARAMS", "Missing or empty 'queries' parameter");

            string format = args.Value<string>("format") ?? "separated";

            try
            {
                // Parse query specifications
                var queries = new List<QuerySpec>();
                for (int i = 0; i < queriesArray.Count; i++)
                {
                    var queryObj = (JObject)queriesArray[i];
                    queries.Add(new QuerySpec
                    {
                        Query = queryObj.Value<string>("query"),
                        Type = queryObj.Value<string>("type") ?? "select",
                        Label = queryObj.Value<string>("label") ?? string.Format("Query {0}", i + 1),
                        MaxRows = queryObj.Value<int?>("max_rows") ?? 1000
                    });
                }

                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    var output = new StringBuilder();
                    int successCount = 0;

                    for (int i = 0; i < queries.Count; i++)
                    {
                        var spec = queries[i];

                        try
                        {
                            if (format == "separated")
                            {
                                output.AppendLine("==============================================");
                                output.AppendLine(string.Format("Query {0}/{1}: {2}", i + 1, queries.Count, spec.Label));
                                output.AppendLine("==============================================");
                                output.AppendLine();
                            }
                            else
                            {
                                output.AppendLine(string.Format("### {0} ###", spec.Label));
                            }

                            if (spec.Type == "select")
                            {
                                using (var cmd = new MySqlCommand(spec.Query, conn))
                                {
                                    cmd.CommandTimeout = 30;
                                    using (var reader = cmd.ExecuteReader())
                                    {
                                        var dt = new DataTable();
                                        dt.Load(reader);

                                        if (dt.Rows.Count > spec.MaxRows)
                                        {
                                            output.AppendLine(string.Format("Note: Limiting output to {0} rows (total: {1})",
                                                spec.MaxRows, dt.Rows.Count));
                                            output.AppendLine();
                                        }

                                        output.Append(MySqlService.FormatDataTable(dt, spec.MaxRows));
                                        output.AppendLine();
                                    }
                                }
                            }
                            else if (spec.Type == "scalar")
                            {
                                using (var cmd = new MySqlCommand(spec.Query, conn))
                                {
                                    cmd.CommandTimeout = 30;
                                    object result = cmd.ExecuteScalar();
                                    output.AppendLine(string.Format("Result: {0}", result ?? "NULL"));
                                    output.AppendLine();
                                }
                            }
                            else if (spec.Type == "execute")
                            {
                                using (var cmd = new MySqlCommand(spec.Query, conn))
                                {
                                    cmd.CommandTimeout = 30;
                                    int affected = cmd.ExecuteNonQuery();
                                    output.AppendLine(string.Format("Rows affected: {0}", affected));
                                    output.AppendLine();
                                }
                            }

                            successCount++;
                        }
                        catch (MySqlException ex)
                        {
                            output.AppendLine(string.Format("ERROR: {0}", ex.Message));
                            output.AppendLine();
                        }

                        if (format == "separated")
                            output.AppendLine();
                    }

                    if (format == "separated")
                    {
                        output.AppendLine("==============================================");
                        output.AppendLine(string.Format("Summary: {0}/{1} queries executed successfully",
                            successCount, queries.Count));
                        output.AppendLine("==============================================");
                    }

                    return ToolResult.Success(output.ToString());
                }
            }
            catch (MySqlException ex)
            {
                return ToolResult.Error("MYSQL_ERROR", ex.Message);
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        private class QuerySpec
        {
            public string Query { get; set; }
            public string Type { get; set; }
            public string Label { get; set; }
            public int MaxRows { get; set; }
        }
    }
}
