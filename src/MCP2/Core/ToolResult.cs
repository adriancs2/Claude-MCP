using System;
using System.Collections.Generic;

namespace MCP2.Core
{
    /// <summary>
    /// Represents the result of a tool execution
    /// </summary>
    public class ToolResult
    {
        public bool IsError { get; set; }
        public string Content { get; set; }
        public List<ToolContent> ContentList { get; set; }

        private ToolResult() { }

        /// <summary>
        /// Create a successful text result
        /// </summary>
        public static ToolResult Success(string content)
        {
            return new ToolResult
            {
                IsError = false,
                Content = content,
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = content }
                }
            };
        }

        /// <summary>
        /// Create an error result with message only
        /// </summary>
        public static ToolResult Error(string message)
        {
            return new ToolResult
            {
                IsError = true,
                Content = message,
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = $"Error: {message}" }
                }
            };
        }

        /// <summary>
        /// Create an error result with code and message
        /// </summary>
        public static ToolResult Error(string code, string message)
        {
            return new ToolResult
            {
                IsError = true,
                Content = $"[{code}] {message}",
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = $"Error [{code}]: {message}" }
                }
            };
        }

        /// <summary>
        /// Create a successful result from a structured object (serialized as JSON)
        /// </summary>
        public static ToolResult SuccessJson(object obj)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
            return new ToolResult
            {
                IsError = false,
                Content = json,
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = json }
                }
            };
        }

        /// <summary>
        /// Create an error result from a structured object (serialized as JSON)
        /// </summary>
        public static ToolResult ErrorJson(object obj)
        {
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj, Newtonsoft.Json.Formatting.Indented);
            return new ToolResult
            {
                IsError = true,
                Content = json,
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = json }
                }
            };
        }

        /// <summary>
        /// Create an image result
        /// </summary>
        public static ToolResult Image(string base64Data, string mimeType = "image/png")
        {
            return new ToolResult
            {
                IsError = false,
                Content = $"[Image: {mimeType}]",
                ContentList = new List<ToolContent>
                {
                    new ToolContent 
                    { 
                        Type = "image",
                        Data = base64Data,
                        MimeType = mimeType
                    }
                }
            };
        }

        /// <summary>
        /// Create a result with both text and image content
        /// </summary>
        public static ToolResult SuccessWithImage(string text, string base64Data, string mimeType = "image/png")
        {
            return new ToolResult
            {
                IsError = false,
                Content = text,
                ContentList = new List<ToolContent>
                {
                    new ToolContent { Type = "text", Text = text },
                    new ToolContent 
                    { 
                        Type = "image",
                        Data = base64Data,
                        MimeType = mimeType
                    }
                }
            };
        }
    }

    /// <summary>
    /// Represents a content item in a tool result (text or image)
    /// </summary>
    public class ToolContent
    {
        public string Type { get; set; }
        public string Text { get; set; }
        public string Data { get; set; }
        public string MimeType { get; set; }
    }
}