using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace MCP2.Core
{
    /// <summary>
    /// Discovers and registers all ITool implementations in the assembly
    /// </summary>
    public static class ToolDiscovery
    {
        /// <summary>
        /// Scan assembly for all ITool implementations and instantiate them
        /// </summary>
        public static Dictionary<string, ITool> DiscoverTools()
        {
            var tools = new Dictionary<string, ITool>();
            var assembly = Assembly.GetExecutingAssembly();

            // Find all types that implement ITool
            var toolTypes = assembly.GetTypes()
                .Where(t => typeof(ITool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in toolTypes)
            {
                try
                {
                    // Instantiate the tool
                    var tool = (ITool)Activator.CreateInstance(type);
                    
                    // Register by name
                    if (!string.IsNullOrEmpty(tool.Name))
                    {
                        tools[tool.Name] = tool;
                    }
                }
                catch (Exception ex)
                {
                    // Log but continue - don't let one bad tool kill discovery
                    Console.Error.WriteLine($"Failed to instantiate tool {type.Name}: {ex.Message}");
                }
            }

            return tools;
        }

        /// <summary>
        /// Generate MCP tool definitions from discovered tools
        /// </summary>
        public static List<McpToolDefinition> GenerateToolDefinitions(Dictionary<string, ITool> tools)
        {
            var definitions = new List<McpToolDefinition>();

            foreach (var tool in tools.Values)
            {
                var definition = new McpToolDefinition
                {
                    Name = tool.Name,
                    Description = tool.Description,
                    InputSchema = new McpInputSchema
                    {
                        Type = "object",
                        Properties = new Dictionary<string, McpPropertySchema>(),
                        Required = new List<string>()
                    }
                };

                // Convert tool parameters to JSON schema properties
                foreach (var param in tool.Params.Parameters)
                {
                    var propertySchema = new McpPropertySchema
                    {
                        Type = param.Type,
                        Description = param.Description
                    };

                    // Add enum values if present
                    if (param.EnumValues != null && param.EnumValues.Length > 0)
                    {
                        propertySchema.Enum = param.EnumValues;
                    }

                    // Add default value if present
                    if (!string.IsNullOrEmpty(param.DefaultValue))
                    {
                        // Try to parse as appropriate type
                        switch (param.Type)
                        {
                            case "integer":
                                if (int.TryParse(param.DefaultValue, out int intVal))
                                    propertySchema.Default = intVal;
                                break;
                            case "boolean":
                                if (bool.TryParse(param.DefaultValue, out bool boolVal))
                                    propertySchema.Default = boolVal;
                                break;
                            default:
                                propertySchema.Default = param.DefaultValue;
                                break;
                        }
                    }

                    definition.InputSchema.Properties[param.Name] = propertySchema;

                    // Add to required list if necessary
                    if (param.Required)
                    {
                        definition.InputSchema.Required.Add(param.Name);
                    }
                }

                // Remove required array if empty
                if (definition.InputSchema.Required.Count == 0)
                {
                    definition.InputSchema.Required = null;
                }

                definitions.Add(definition);
            }

            return definitions;
        }
    }
}