using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using MCP2.Core;
using Newtonsoft.Json;

namespace MCP2.Services
{
    /// <summary>
    /// Shared HTTP request execution. Used by HttpGet, HttpPost, HttpRequest tools.
    /// Synchronous wrapper around HttpClient for MCP's synchronous design.
    /// </summary>
    public static class HttpService
    {
        private static readonly HttpClient _httpClient;

        static HttpService()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };
            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MCP2/1.0");
        }

        public static ToolResult ExecuteRequest(string url, string method, string bodyType,
            string payload, string returnType, string outputPath, string headersJson)
        {
            try
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri uri))
                    return ToolResult.Error("INVALID_URL", "Invalid URL format: " + url);

                var request = new HttpRequestMessage(new HttpMethod(method), uri);

                // Add custom headers
                if (!string.IsNullOrEmpty(headersJson))
                {
                    try
                    {
                        var headers = JsonConvert.DeserializeObject<Dictionary<string, string>>(headersJson);
                        foreach (var header in headers)
                            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }
                    catch (Exception ex)
                    {
                        return ToolResult.Error("INVALID_HEADERS", "Failed to parse headers JSON: " + ex.Message);
                    }
                }

                // Add body for POST/PUT/PATCH
                if (!string.IsNullOrEmpty(payload) && (method == "POST" || method == "PUT" || method == "PATCH"))
                {
                    if (bodyType != null && bodyType.ToLowerInvariant() == "form")
                    {
                        try
                        {
                            var formData = JsonConvert.DeserializeObject<Dictionary<string, string>>(payload);
                            request.Content = new FormUrlEncodedContent(formData);
                        }
                        catch (Exception ex)
                        {
                            return ToolResult.Error("INVALID_PAYLOAD", "Failed to parse form data JSON: " + ex.Message);
                        }
                    }
                    else
                    {
                        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                    }
                }

                // Send request synchronously
                var response = _httpClient.SendAsync(request).Result;

                // Build response info
                var info = new StringBuilder();
                info.AppendLine(string.Format("HTTP {0} {1}", (int)response.StatusCode, response.StatusCode));
                info.AppendLine(string.Format("URL: {0}", url));

                string contentType = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
                info.AppendLine(string.Format("Content-Type: {0}", contentType));

                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue)
                    info.AppendLine(string.Format("Content-Length: {0} bytes", contentLength.Value));

                info.AppendLine();

                bool isTextContent = IsTextContentType(contentType);
                bool forceText = returnType != null && returnType.ToLowerInvariant() == "text";
                bool forceFile = returnType != null && returnType.ToLowerInvariant() == "file";

                if (forceFile || (!isTextContent && !forceText))
                {
                    // Save as file
                    string filePath = outputPath;

                    if (string.IsNullOrEmpty(filePath))
                    {
                        string defaultDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "html");
                        string filename = GetFilenameFromResponse(response, contentType);
                        filePath = Path.Combine(defaultDir, filename);
                    }

                    string dir = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        Directory.CreateDirectory(dir);

                    byte[] fileBytes = response.Content.ReadAsByteArrayAsync().Result;
                    File.WriteAllBytes(filePath, fileBytes);

                    info.AppendLine(string.Format("Saved to: {0}", filePath));
                    info.AppendLine(string.Format("File size: {0} bytes", fileBytes.Length));

                    return ToolResult.Success(info.ToString());
                }
                else
                {
                    // Return as text
                    string content = response.Content.ReadAsStringAsync().Result;

                    if (content.Length > 50000)
                    {
                        info.AppendLine("[Response truncated to 50000 characters]");
                        info.AppendLine();
                        info.Append(content.Substring(0, 50000));
                        info.AppendLine("...");
                    }
                    else
                    {
                        info.Append(content);
                    }

                    return ToolResult.Success(info.ToString());
                }
            }
            catch (AggregateException ae) when (ae.InnerException is TaskCanceledException)
            {
                return ToolResult.Error("TIMEOUT", "Request timed out after 30 seconds");
            }
            catch (AggregateException ae) when (ae.InnerException is HttpRequestException)
            {
                return ToolResult.Error("REQUEST_FAILED", "HTTP request failed: " + ae.InnerException.Message);
            }
            catch (Exception ex)
            {
                return ToolResult.Error("ERROR", ex.Message);
            }
        }

        private static bool IsTextContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return false;
            contentType = contentType.ToLowerInvariant();
            if (contentType.StartsWith("text/")) return true;
            if (contentType.Contains("json")) return true;
            if (contentType.Contains("xml")) return true;
            if (contentType.Contains("javascript")) return true;
            return false;
        }

        private static string GetFilenameFromResponse(HttpResponseMessage response, string contentType)
        {
            var contentDisposition = response.Content.Headers.ContentDisposition;
            if (contentDisposition != null && !string.IsNullOrEmpty(contentDisposition.FileName))
                return SanitizeFilename(contentDisposition.FileName.Trim('"'));

            string urlPath = response.RequestMessage.RequestUri.AbsolutePath;
            string urlFilename = Path.GetFileName(urlPath);
            if (!string.IsNullOrEmpty(urlFilename) && urlFilename.Contains("."))
                return SanitizeFilename(urlFilename);

            string extension = GetExtensionFromContentType(contentType);
            return string.Format("file-download-{0}{1}", DateTime.Now.ToString("yyyyMMddHHmmss"), extension);
        }

        private static string GetExtensionFromContentType(string contentType)
        {
            if (string.IsNullOrEmpty(contentType)) return ".bin";
            contentType = contentType.ToLowerInvariant();

            if (contentType.Contains("json")) return ".json";
            if (contentType.Contains("xml")) return ".xml";
            if (contentType.Contains("html")) return ".html";
            if (contentType.Contains("css")) return ".css";
            if (contentType.Contains("javascript")) return ".js";
            if (contentType.Contains("csv")) return ".csv";
            if (contentType.Contains("pdf")) return ".pdf";
            if (contentType.Contains("zip")) return ".zip";
            if (contentType.Contains("gzip")) return ".gz";
            if (contentType.Contains("jpeg") || contentType.Contains("jpg")) return ".jpg";
            if (contentType.Contains("png")) return ".png";
            if (contentType.Contains("gif")) return ".gif";
            if (contentType.Contains("webp")) return ".webp";
            if (contentType.Contains("svg")) return ".svg";
            if (contentType == "text/plain") return ".txt";
            return ".bin";
        }

        private static string SanitizeFilename(string filename)
        {
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in invalid)
                filename = filename.Replace(c, '_');
            return filename;
        }
    }
}
