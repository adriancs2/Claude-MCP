using MCP2.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MCP2
{
    /// <summary>
    /// Main MCP server that handles JSON-RPC protocol over stdin/stdout
    /// </summary>
    public class McpServer
    {
        private Dictionary<string, ITool> _tools;
        private StreamWriter _logWriter;
        private bool _initialized = false;

        public McpServer()
        {
            // Discover all tools
            _tools = ToolDiscovery.DiscoverTools();

            // Setup debug logging if enabled
            if (McpConfig.DebugLogging)
            {
                string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mcp_debug.log");
                _logWriter = new StreamWriter(logPath, append: true, Encoding.UTF8);
                _logWriter.AutoFlush = true;
                LogDebug($"=== MCP Server Started at {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
                LogDebug($"Discovered {_tools.Count} tools");
            }
        }

        /// <summary>
        /// Start the server and enter the main message loop
        /// </summary>
        public void Start()
        {
            LogDebug("Entering main message loop");

            while (true)
            {
                try
                {
                    // Read line from stdin
                    string line = Console.ReadLine();
                    
                    if (line == null)
                    {
                        // EOF reached, exit gracefully
                        LogDebug("EOF received, shutting down");
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    LogDebug($"Received: {line}");

                    // Parse JSON-RPC request
                    JsonRpcRequest request = JsonConvert.DeserializeObject<JsonRpcRequest>(line);
                    
                    // Handle the request
                    HandleRequest(request);
                }
                catch (JsonException ex)
                {
                    LogDebug($"JSON parse error: {ex.Message}");
                    SendError(null, JsonRpcErrorCodes.ParseError, "Parse error", ex.Message);
                }
                catch (Exception ex)
                {
                    LogDebug($"Unexpected error: {ex.Message}");
                    SendError(null, JsonRpcErrorCodes.InternalError, "Internal error", ex.ToString());
                }
            }

            // Cleanup
            if (_logWriter != null)
            {
                LogDebug("=== MCP Server Stopped ===");
                _logWriter.Close();
            }
        }

        /// <summary>
        /// Handle a JSON-RPC request
        /// </summary>
        private void HandleRequest(JsonRpcRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.Method))
            {
                SendError(request?.Id, JsonRpcErrorCodes.InvalidRequest, "Invalid request", null);
                return;
            }

            LogDebug($"Handling method: {request.Method}");

            try
            {
                switch (request.Method)
                {
                    case "initialize":
                        HandleInitialize(request);
                        break;

                    case "notifications/initialized":
                        // Client confirms initialization - no response needed
                        LogDebug("Client initialization confirmed");
                        break;

                    case "notifications/cancelled":
                        // Request cancellation - log but ignore (we don't support cancellation)
                        LogDebug("Cancellation notification received (ignored)");
                        break;

                    case "tools/list":
                        HandleToolsList(request);
                        break;

                    case "tools/call":
                        HandleToolCall(request);
                        break;

                    case "ping":
                        HandlePing(request);
                        break;

                    default:
                        SendError(request.Id, JsonRpcErrorCodes.MethodNotFound, 
                            $"Method not found: {request.Method}", null);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogDebug($"Error handling {request.Method}: {ex.Message}");
                SendError(request.Id, JsonRpcErrorCodes.InternalError, "Internal error", ex.Message);
            }
        }

        /// <summary>
        /// Handle initialize method
        /// </summary>
        private void HandleInitialize(JsonRpcRequest request)
        {
            _initialized = true;

            // Load server instructions if available
            string instructions = null;
            string instructionsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "system-prompts.txt");
            if (System.IO.File.Exists(instructionsPath))
            {
                try
                {
                    instructions = System.IO.File.ReadAllText(instructionsPath, System.Text.Encoding.UTF8);
                    if (string.IsNullOrWhiteSpace(instructions))
                        instructions = null;
                    LogDebug($"Loaded server instructions ({instructions?.Length ?? 0} chars)");
                }
                catch (Exception ex)
                {
                    LogDebug($"Failed to load server instructions: {ex.Message}");
                }
            }

            var result = new InitializeResult
            {
                ProtocolVersion = "2025-11-25",
                Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability()
                },
                ServerInfo = new ServerInfo
                {
                    Name = "MCP2",
                    Version = "1.0.0"
                },
                Instructions = instructions
            };

            SendResponse(request.Id, result);
        }

        /// <summary>
        /// Handle tools/list method
        /// </summary>
        private void HandleToolsList(JsonRpcRequest request)
        {
            if (!_initialized)
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidRequest, 
                    "Server not initialized", null);
                return;
            }

            var definitions = ToolDiscovery.GenerateToolDefinitions(_tools);
            var result = new ToolsListResult { Tools = definitions };

            SendResponse(request.Id, result);
        }

        /// <summary>
        /// Handle tools/call method
        /// </summary>
        private void HandleToolCall(JsonRpcRequest request)
        {
            if (!_initialized)
            {
                SendError(request.Id, JsonRpcErrorCodes.InvalidRequest, 
                    "Server not initialized", null);
                return;
            }

            try
            {
                // Parse tool call parameters
                var toolParams = request.Params.ToObject<ToolCallParams>();
                
                if (string.IsNullOrEmpty(toolParams.Name))
                {
                    SendError(request.Id, JsonRpcErrorCodes.InvalidParams, 
                        "Tool name is required", null);
                    return;
                }

                // Find the tool
                if (!_tools.TryGetValue(toolParams.Name, out ITool tool))
                {
                    SendError(request.Id, JsonRpcErrorCodes.InvalidParams, 
                        $"Unknown tool: {toolParams.Name}", null);
                    return;
                }

                LogDebug($"Executing tool: {toolParams.Name}");

                // Extract arguments directly as JObject from raw params to preserve
                // integer types (avoids Dictionary<string,object> converting int to long,
                // which breaks args.Value<int?> lookups via JObject.FromObject)
                JObject args = request.Params["arguments"] as JObject ?? new JObject();

                // Execute tool with safety net (Section 1.7 of spec)
                ToolResult toolResult;
                try
                {
                    toolResult = tool.Execute(args);
                }
                catch (UnauthorizedAccessException ex)
                {
                    LogDebug($"Tool access denied: {ex.Message}");
                    toolResult = ToolResult.Error(ex.Message);
                }
                catch (System.IO.FileNotFoundException ex)
                {
                    LogDebug($"Tool file not found: {ex.Message}");
                    toolResult = ToolResult.Error(ex.Message);
                }
                catch (System.IO.IOException ex)
                {
                    LogDebug($"Tool IO error: {ex.Message}");
                    toolResult = ToolResult.Error(ex.Message);
                }
                catch (ArgumentOutOfRangeException ex)
                {
                    LogDebug($"Tool argument out of range: {ex.Message}");
                    toolResult = ToolResult.Error(ex.Message);
                }
                catch (ArgumentException ex)
                {
                    LogDebug($"Tool invalid params: {ex.Message}");
                    toolResult = ToolResult.Error("INVALID_PARAMS", ex.Message);
                }
                catch (Exception ex)
                {
                    LogDebug($"Tool execution error: {ex.Message}");
                    toolResult = ToolResult.Error("INTERNAL_ERROR", ex.Message);
                }

                // Convert to MCP format
                var mcpResult = new ToolCallResult
                {
                    Content = toolResult.ContentList.Select(c => new ToolContentItem
                    {
                        Type = c.Type,
                        Text = c.Text,
                        Data = c.Data,
                        MimeType = c.MimeType
                    }).ToList(),
                    IsError = toolResult.IsError ? (bool?)true : null
                };

                // Append warning if config file is using default values
                if (McpConfig.UsingDefaults)
                {
                    mcpResult.Content.Add(new ToolContentItem
                    {
                        Type = "text",
                        Text = string.Format(
                            "\n\n⚠️ mcp-config.json is using default values. Please review and update: {0}",
                            McpConfig.ConfigFilePath)
                    });
                }

                SendResponse(request.Id, mcpResult);

                // GC management (Section 1.6 of spec)
                PerformGcIfNeeded();
            }
            catch (JsonException ex)
            {
                LogDebug($"Parameter parse error: {ex.Message}");
                SendError(request.Id, JsonRpcErrorCodes.InvalidParams, 
                    "Invalid parameters", ex.Message);
            }
        }

        /// <summary>
        /// Handle ping method
        /// </summary>
        private void HandlePing(JsonRpcRequest request)
        {
            SendResponse(request.Id, new { });
        }

        /// <summary>
        /// Perform garbage collection if memory threshold exceeded
        /// </summary>
        private void PerformGcIfNeeded()
        {
            long memoryBytes = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64;
            long memoryMb = memoryBytes / (1024 * 1024);
            long thresholdBytes = (long)McpConfig.GcMemoryThresholdMb * 1024 * 1024;
            
            if (memoryBytes > thresholdBytes)
            {
                LogDebug($"Memory threshold exceeded ({memoryMb}MB > {McpConfig.GcMemoryThresholdMb}MB), performing GC");
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true);
                
                long afterMb = System.Diagnostics.Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024);
                LogDebug($"GC complete, memory: {afterMb}MB");
            }
        }

        /// <summary>
        /// Send a successful JSON-RPC response
        /// </summary>
        private void SendResponse(object id, object result)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Result = result
            };

            string json = JsonConvert.SerializeObject(response);
            LogDebug($"Sending response: {json}");
            Console.WriteLine(json);
            Console.Out.Flush();
        }

        /// <summary>
        /// Send a JSON-RPC error response
        /// </summary>
        private void SendError(object id, int code, string message, object data)
        {
            var response = new JsonRpcResponse
            {
                Id = id,
                Error = new JsonRpcError
                {
                    Code = code,
                    Message = message,
                    Data = data
                }
            };

            string json = JsonConvert.SerializeObject(response);
            LogDebug($"Sending error: {json}");
            Console.WriteLine(json);
            Console.Out.Flush();
        }

        /// <summary>
        /// Write debug log entry if logging enabled
        /// </summary>
        private void LogDebug(string message)
        {
            if (_logWriter != null)
            {
                _logWriter.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
            }
        }
    }
}