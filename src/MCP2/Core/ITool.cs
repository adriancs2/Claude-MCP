using Newtonsoft.Json.Linq;
using System.Collections.Generic;

namespace MCP2.Core
{
    /// <summary>
    /// Interface that all MCP tools must implement
    /// </summary>
    public interface ITool
    {
        /// <summary>
        /// Unique tool name (kebab-case)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Human-readable description of what the tool does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// Parameter definitions for this tool
        /// </summary>
        ToolParamList Params { get; }

        /// <summary>
        /// Execute the tool with the provided arguments
        /// </summary>
        /// <param name="args">JSON object containing the tool arguments</param>
        /// <returns>Result of the tool execution</returns>
        ToolResult Execute(JObject args);
    }

    /// <summary>
    /// Fluent builder for defining tool parameters
    /// </summary>
    public class ToolParamList
    {
        private readonly List<ToolParam> _params = new List<ToolParam>();

        public List<ToolParam> Parameters => _params;

        /// <summary>
        /// Add a string parameter
        /// </summary>
        public ToolParamList String(string name, string description, bool required = false, string defaultValue = null)
        {
            _params.Add(new ToolParam
            {
                Name = name,
                Type = "string",
                Description = description,
                Required = required,
                DefaultValue = defaultValue
            });
            return this;
        }

        /// <summary>
        /// Add a string parameter with enum values
        /// </summary>
        public ToolParamList StringEnum(string name, string description, string[] enumValues, bool required = false, string defaultValue = null)
        {
            _params.Add(new ToolParam
            {
                Name = name,
                Type = "string",
                Description = description,
                Required = required,
                DefaultValue = defaultValue,
                EnumValues = enumValues
            });
            return this;
        }

        /// <summary>
        /// Add an integer parameter
        /// </summary>
        public ToolParamList Int(string name, string description, bool required = false, int? defaultValue = null)
        {
            _params.Add(new ToolParam
            {
                Name = name,
                Type = "integer",
                Description = description,
                Required = required,
                DefaultValue = defaultValue?.ToString()
            });
            return this;
        }

        /// <summary>
        /// Add a boolean parameter
        /// </summary>
        public ToolParamList Bool(string name, string description, bool required = false, bool? defaultValue = null)
        {
            _params.Add(new ToolParam
            {
                Name = name,
                Type = "boolean",
                Description = description,
                Required = required,
                DefaultValue = defaultValue?.ToString().ToLower()
            });
            return this;
        }

        /// <summary>
        /// Add an array parameter
        /// </summary>
        public ToolParamList Array(string name, string description, bool required = false)
        {
            _params.Add(new ToolParam
            {
                Name = name,
                Type = "array",
                Description = description,
                Required = required
            });
            return this;
        }

        /// <summary>
        /// Add an object parameter
        /// </summary>
        public ToolParamList Object(string name, string description, bool required = false)
        {
            _params.Add(new ToolParam
            {
                Name = name,
                Type = "object",
                Description = description,
                Required = required
            });
            return this;
        }
    }

    /// <summary>
    /// Represents a single tool parameter definition
    /// </summary>
    public class ToolParam
    {
        public string Name { get; set; }
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public string DefaultValue { get; set; }
        public string[] EnumValues { get; set; }
    }
}