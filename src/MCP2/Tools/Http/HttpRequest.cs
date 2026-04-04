using System;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Http
{
    public class HttpRequest : ITool
    {
        public string Name => "http_request";
        public string Description => "Send a generic HTTP request with any method (GET, POST, PUT, DELETE, PATCH, etc.).";

        public ToolParamList Params => new ToolParamList()
            .String("url", "The URL to request", required: true)
            .String("method", "HTTP method: GET, POST, PUT, DELETE, PATCH, HEAD, OPTIONS (default: GET)")
            .String("body_type", "Body format: 'json' (default) or 'form' (for POST/PUT/PATCH)")
            .String("payload", "Request body content")
            .String("return_type", "Response handling: 'auto' (default), 'text', 'file'")
            .String("output_path", "File path to save response")
            .String("headers", "Custom headers as JSON object");

        public ToolResult Execute(JObject args)
        {
            string url = args.Value<string>("url");
            if (string.IsNullOrEmpty(url))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'url' parameter");

            string method = (args.Value<string>("method") ?? "GET").ToUpperInvariant();
            string bodyType = args.Value<string>("body_type") ?? "json";
            string payload = args.Value<string>("payload");
            string returnType = args.Value<string>("return_type") ?? "auto";
            string outputPath = args.Value<string>("output_path");
            string headersJson = args.Value<string>("headers");

            return HttpService.ExecuteRequest(url, method, bodyType, payload, returnType, outputPath, headersJson);
        }
    }
}
