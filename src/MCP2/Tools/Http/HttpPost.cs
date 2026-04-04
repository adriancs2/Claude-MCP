using System;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Http
{
    public class HttpPost : ITool
    {
        public string Name => "http_post";
        public string Description => "Send an HTTP POST request with form data or JSON body. Returns response as text or saves to file.";

        public ToolParamList Params => new ToolParamList()
            .String("url", "The URL to request", required: true)
            .String("body_type", "Body format: 'json' (default) or 'form'")
            .String("payload", "Request body - JSON string for 'json' type, or JSON object of key-value pairs for 'form' type")
            .String("return_type", "Response handling: 'auto' (default), 'text' (force text), 'file' (save to file)")
            .String("output_path", "File path to save response (required if return_type is 'file')")
            .String("headers", "Custom headers as JSON object (optional)");

        public ToolResult Execute(JObject args)
        {
            string url = args.Value<string>("url");
            if (string.IsNullOrEmpty(url))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'url' parameter");

            string bodyType = args.Value<string>("body_type") ?? "json";
            string payload = args.Value<string>("payload");
            string returnType = args.Value<string>("return_type") ?? "auto";
            string outputPath = args.Value<string>("output_path");
            string headersJson = args.Value<string>("headers");

            return HttpService.ExecuteRequest(url, "POST", bodyType, payload, returnType, outputPath, headersJson);
        }
    }
}
