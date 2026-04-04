using Newtonsoft.Json;
using System.Collections.Generic;

namespace MCP2.Core
{
    /// <summary>
    /// Result of the initialize method
    /// </summary>
    public class InitializeResult
    {
        [JsonProperty("protocolVersion")]
        public string ProtocolVersion { get; set; }

        [JsonProperty("capabilities")]
        public ServerCapabilities Capabilities { get; set; }

        [JsonProperty("serverInfo")]
        public ServerInfo ServerInfo { get; set; }

        [JsonProperty("instructions", NullValueHandling = NullValueHandling.Ignore)]
        public string Instructions { get; set; }
    }

    /// <summary>
    /// Server information
    /// </summary>
    public class ServerInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    /// <summary>
    /// Server capabilities
    /// </summary>
    public class ServerCapabilities
    {
        [JsonProperty("tools", NullValueHandling = NullValueHandling.Ignore)]
        public ToolsCapability Tools { get; set; }
    }

    /// <summary>
    /// Tools capability
    /// </summary>
    public class ToolsCapability
    {
        // Empty object to indicate tools are supported
    }

    /// <summary>
    /// Client capabilities (received during initialize)
    /// </summary>
    public class ClientCapabilities
    {
        [JsonProperty("sampling", NullValueHandling = NullValueHandling.Ignore)]
        public object Sampling { get; set; }
    }

    /// <summary>
    /// MCP tool definition sent to client
    /// </summary>
    public class McpToolDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("inputSchema")]
        public McpInputSchema InputSchema { get; set; }
    }

    /// <summary>
    /// JSON Schema for tool input
    /// </summary>
    public class McpInputSchema
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";

        [JsonProperty("properties")]
        public Dictionary<string, McpPropertySchema> Properties { get; set; }

        [JsonProperty("required", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Required { get; set; }
    }

    /// <summary>
    /// JSON Schema for a single property
    /// </summary>
    public class McpPropertySchema
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("default", NullValueHandling = NullValueHandling.Ignore)]
        public object Default { get; set; }

        [JsonProperty("enum", NullValueHandling = NullValueHandling.Ignore)]
        public string[] Enum { get; set; }

        [JsonProperty("items", NullValueHandling = NullValueHandling.Ignore)]
        public McpPropertySchema Items { get; set; }
    }

    /// <summary>
    /// Result of tools/list method
    /// </summary>
    public class ToolsListResult
    {
        [JsonProperty("tools")]
        public List<McpToolDefinition> Tools { get; set; }
    }

    /// <summary>
    /// Parameters for tools/call method
    /// </summary>
    public class ToolCallParams
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("arguments")]
        public Dictionary<string, object> Arguments { get; set; }
    }

    /// <summary>
    /// Result of tools/call method
    /// </summary>
    public class ToolCallResult
    {
        [JsonProperty("content")]
        public List<ToolContentItem> Content { get; set; }

        [JsonProperty("isError", NullValueHandling = NullValueHandling.Ignore)]
        public bool? IsError { get; set; }
    }

    /// <summary>
    /// Content item in tool call result
    /// </summary>
    public class ToolContentItem
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("text", NullValueHandling = NullValueHandling.Ignore)]
        public string Text { get; set; }

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public string Data { get; set; }

        [JsonProperty("mimeType", NullValueHandling = NullValueHandling.Ignore)]
        public string MimeType { get; set; }
    }
}