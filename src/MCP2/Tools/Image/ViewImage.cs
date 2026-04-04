using MCP2.Core;
using MCP2.Services;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace MCP2.Tools.Image
{
    public class ViewImage : ITool
    {
        public string Name => "view_image";
        public string Description => "Read an image file and return it as base64 data that Claude can see and analyze. Supported formats: PNG, JPG, JPEG, GIF, WEBP, BMP. Maximum file size: 10MB.";

        public ToolParamList Params => new ToolParamList()
            .String("path", "Full path to the image file", required: true);

        private static readonly HashSet<string> SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        };

        private static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".png", "image/png" },
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif", "image/gif" },
            { ".webp", "image/webp" },
            { ".bmp", "image/bmp" }
        };

        private const long MaxFileSize = 10 * 1024 * 1024; // 10MB

        public ToolResult Execute(JObject args)
        {
            string path = args.Value<string>("path");
            if (string.IsNullOrEmpty(path))
                return ToolResult.Error("INVALID_PARAMS", "Missing 'path' parameter");

            

            if (!System.IO.File.Exists(path))
                return ToolResult.Error($"File not found: {path}");

            string ext = System.IO.Path.GetExtension(path);
            if (string.IsNullOrEmpty(ext) || !SupportedExtensions.Contains(ext))
                return ToolResult.Error($"Unsupported image format: {ext}. Supported: PNG, JPG, JPEG, GIF, WEBP, BMP");

            var fileInfo = new System.IO.FileInfo(path);
            if (fileInfo.Length > MaxFileSize)
                return ToolResult.Error($"File too large: {fileInfo.Length / (1024 * 1024.0):F1}MB. Maximum: 10MB");

            string mimeType = MimeTypes[ext];
            byte[] imageBytes = System.IO.File.ReadAllBytes(path);
            string base64 = Convert.ToBase64String(imageBytes);

            return ToolResult.Image(base64, mimeType);
        }
    }
}
