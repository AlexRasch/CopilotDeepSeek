using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Routes
{
    public static class OllamaRoutes
    {
        //Ollama health check
        public static async Task HandleOllamaHealthCheck(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                var healthBytes = """{"status":"ollama is running"}"""u8;
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = healthBytes.Length;
                byte[] messageBytes = healthBytes.ToArray();
                await context.Response.OutputStream.WriteAsync(messageBytes, 0, messageBytes.Length);
            }
            catch { }
        }

        public static async Task HandleOllamaTagsAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                using var response = await ctx.HttpClient.GetAsync($"{ctx.TargetBase}/models");

                if (!response.IsSuccessStatusCode)
                {
                    return;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                using var ms = new MemoryStream();
                using var writer = new Utf8JsonWriter(ms);

                writer.WriteStartObject();
                writer.WritePropertyName("models");
                writer.WriteStartArray();

                if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var model in data.EnumerateArray())
                    {
                        var id = model.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                        if (string.IsNullOrEmpty(id)) continue;

                        var modelTag = $"{id}";
                        var digest = Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                Encoding.UTF8.GetBytes(id)))
                            .ToLowerInvariant();

                        writer.WriteStartObject();
                        writer.WriteString("name", modelTag);
                        writer.WriteString("model", modelTag);
                        writer.WriteString("modified_at", "2024-01-01T00:00:00Z");
                        writer.WriteNumber("size", 3821945920L);
                        writer.WriteString("digest", $"sha256:{digest}");

                        writer.WriteStartObject("details");
                        writer.WriteString("parent_model", "");
                        writer.WriteString("format", "gguf");
                        writer.WriteString("family", id.Split('-')[0]);
                        writer.WriteStartArray("families");
                        writer.WriteStringValue("deepseek");
                        writer.WriteEndArray();
                        writer.WriteString("parameter_size", "7B");
                        writer.WriteString("quantization_level", "Q4_K_M");
                        writer.WriteEndObject();

                        writer.WriteEndObject();
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
                writer.Flush();

                var bytes = ms.ToArray();
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 502;
                var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
                await context.Response.OutputStream.WriteAsync(error);
            }
        }
    }
}
