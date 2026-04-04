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
    public class BatchMySqlQueriesWithVars : ITool
    {
        public string Name => "batch_mysql_queries_with_variables";
        public string Description => "Execute multiple MySQL queries with variable passing between queries. Store results from one query (e.g., LAST_INSERT_ID) and use them in subsequent queries. Perfect for complex multi-step operations like invoice creation with line items.";

        public ToolParamList Params => new ToolParamList()
            .Array("queries", "Array of query specifications with query, type, store_as, and optional label", required: true)
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
                var queries = new List<QuerySpecWithVars>();
                for (int i = 0; i < queriesArray.Count; i++)
                {
                    var queryObj = (JObject)queriesArray[i];
                    var storeAs = new Dictionary<string, CellReference>();

                    JToken storeAsToken = queryObj["store_as"];
                    if (storeAsToken != null && storeAsToken.Type == JTokenType.Object)
                    {
                        foreach (JProperty prop in ((JObject)storeAsToken).Properties())
                        {
                            var cellRef = (JObject)prop.Value;
                            storeAs[prop.Name] = new CellReference
                            {
                                Row = cellRef.Value<int>("row"),
                                Col = cellRef.Value<int>("col")
                            };
                        }
                    }

                    queries.Add(new QuerySpecWithVars
                    {
                        Query = queryObj.Value<string>("query"),
                        Type = queryObj.Value<string>("type") ?? "select",
                        Label = queryObj.Value<string>("label") ?? string.Format("Query {0}", i + 1),
                        MaxRows = queryObj.Value<int?>("max_rows") ?? 1000,
                        StoreAs = storeAs
                    });
                }

                using (var conn = new MySqlConnection(connectionString))
                {
                    conn.Open();

                    using (var transaction = conn.BeginTransaction())
                    {
                        var output = new StringBuilder();
                        int successCount = 0;
                        var variables = new Dictionary<string, string>();

                        for (int i = 0; i < queries.Count; i++)
                        {
                            var spec = queries[i];

                            try
                            {
                                string processedQuery = MySqlService.ReplaceVariables(spec.Query, variables);

                                if (format == "separated")
                                {
                                    output.AppendLine("==============================================");
                                    output.AppendLine(string.Format("Query {0}/{1}: {2}", i + 1, queries.Count, spec.Label));
                                    output.AppendLine("==============================================");
                                    output.AppendLine(string.Format("SQL: {0}", processedQuery));
                                    output.AppendLine();
                                }
                                else
                                {
                                    output.AppendLine(string.Format("### {0} ###", spec.Label));
                                    output.AppendLine(string.Format("SQL: {0}", processedQuery));
                                }

                                if (spec.Type == "select")
                                {
                                    using (var cmd = new MySqlCommand(processedQuery, conn, transaction))
                                    {
                                        cmd.CommandTimeout = 30;
                                        using (var reader = cmd.ExecuteReader())
                                        {
                                            var dt = new DataTable();
                                            dt.Load(reader);

                                            // Store variables if specified
                                            if (spec.StoreAs.Count > 0)
                                            {
                                                foreach (var kvp in spec.StoreAs)
                                                {
                                                    if (kvp.Value.Row < dt.Rows.Count && kvp.Value.Col < dt.Columns.Count)
                                                    {
                                                        variables[kvp.Key] = dt.Rows[kvp.Value.Row][kvp.Value.Col]?.ToString() ?? "NULL";
                                                        output.AppendLine(string.Format("Stored: {{{0}}} = {1}", kvp.Key, variables[kvp.Key]));
                                                    }
                                                }
                                                output.AppendLine();
                                            }

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
                                    using (var cmd = new MySqlCommand(processedQuery, conn, transaction))
                                    {
                                        cmd.CommandTimeout = 30;
                                        object result = cmd.ExecuteScalar();
                                        string resultStr = result?.ToString() ?? "NULL";

                                        if (spec.StoreAs.Count > 0)
                                        {
                                            foreach (var kvp in spec.StoreAs)
                                            {
                                                variables[kvp.Key] = resultStr;
                                                output.AppendLine(string.Format("Stored: {{{0}}} = {1}", kvp.Key, variables[kvp.Key]));
                                            }
                                            output.AppendLine();
                                        }

                                        output.AppendLine(string.Format("Result: {0}", resultStr));
                                        output.AppendLine();
                                    }
                                }
                                else if (spec.Type == "execute")
                                {
                                    using (var cmd = new MySqlCommand(processedQuery, conn, transaction))
                                    {
                                        cmd.CommandTimeout = 30;
                                        int affected = cmd.ExecuteNonQuery();

                                        if (spec.StoreAs.Count > 0)
                                        {
                                            foreach (var kvp in spec.StoreAs)
                                            {
                                                variables[kvp.Key] = affected.ToString();
                                                output.AppendLine(string.Format("Stored: {{{0}}} = {1}", kvp.Key, variables[kvp.Key]));
                                            }
                                            output.AppendLine();
                                        }

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

                                transaction.Rollback();

                                if (format == "separated")
                                {
                                    output.AppendLine("==============================================");
                                    output.AppendLine(string.Format("Transaction rolled back due to error in query {0}", i + 1));
                                    output.AppendLine("==============================================");
                                }

                                return ToolResult.Error("QUERY_ERROR", output.ToString());
                            }

                            if (format == "separated")
                                output.AppendLine();
                        }

                        transaction.Commit();

                        if (format == "separated")
                        {
                            output.AppendLine("==============================================");
                            output.AppendLine(string.Format("Summary: {0}/{1} queries executed successfully",
                                successCount, queries.Count));
                            output.AppendLine("Transaction committed successfully");
                            output.AppendLine("==============================================");
                        }

                        return ToolResult.Success(output.ToString());
                    }
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

        private class QuerySpecWithVars
        {
            public string Query { get; set; }
            public string Type { get; set; }
            public string Label { get; set; }
            public int MaxRows { get; set; }
            public Dictionary<string, CellReference> StoreAs { get; set; }
        }

        private class CellReference
        {
            public int Row { get; set; }
            public int Col { get; set; }
        }
    }
}
