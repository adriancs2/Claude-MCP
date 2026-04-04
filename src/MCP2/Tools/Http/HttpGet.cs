using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCP2.Tools.Http
{
    public class HttpGet : ITool
    {
        public string Name => "http_get";
        public string Description => "Send an HTTP GET request. Returns response as text or saves to file based on content type.";

        public ToolParamList Params => new ToolParamList()
            .String("url", "The URL to request", required: true)
            .String("return_type", "Response handling: 'auto' (default), 'text' (force text), 'file' (save to file)")
            .String("output_path", "File path to save response (required if return_type is 'file')")
            .String("headers", "Custom headers as JSON object (optional)");

        public ToolResult Execute(JObject args)
        {
            string url = args.Value<string>("url");
            if (string.IsNullOrEmpty(url))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'url' parameter");

            string returnType = args.Value<string>("return_type") ?? "auto";
            string outputPath = args.Value<string>("output_path");
            string headersJson = args.Value<string>("headers");

            return HttpService.ExecuteRequest(url, "GET", null, null, returnType, outputPath, headersJson);
        }
    }
}
