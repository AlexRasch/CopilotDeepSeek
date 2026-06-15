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

        /// <summary>
        /// Translates Ollama /api/chat requests to DeepSeek /chat/completions
        /// and transforms responses back to Ollama format.
        /// Supports both streaming and non-streaming.
        /// </summary>
        public static async Task HandleChatAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                // Read Ollama-format request body
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                RequestContext.DumpJson("OLLAMA INCOMING", body);

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                // Extract model and streaming flag
                var model = root.TryGetProperty("model", out var m) ? m.GetString() : "";
                var stream = root.TryGetProperty("stream", out var s) && s.GetBoolean();

                // Forward to DeepSeek /chat/completions
                using var client = new HttpClient(ctx.SocketHandler, disposeHandler: false);
                using var forwardRequest = new HttpRequestMessage(HttpMethod.Post, $"{ctx.TargetBase}/chat/completions")
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
                forwardRequest.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.ApiKey);

                var completionOption = stream
                    ? HttpCompletionOption.ResponseHeadersRead
                    : HttpCompletionOption.ResponseContentRead;

                using var forwardResponse = await client.SendAsync(forwardRequest, completionOption);

                context.Response.StatusCode = (int)forwardResponse.StatusCode;
                context.Response.ContentType = "application/json";

                if (stream && forwardResponse.IsSuccessStatusCode)
                {
                    // Stream: transform each DeepSeek SSE chunk → Ollama NDJSON line
                    using var upstreamStream = await forwardResponse.Content.ReadAsStreamAsync();
                    using var upstreamReader = new StreamReader(upstreamStream);

                    while (true)
                    {
                        var line = await upstreamReader.ReadLineAsync();
                        if (line == null) break;

                        if (line.StartsWith("data: ") && line[6..] != "[DONE]")
                        {
                            try
                            {
                                var chunkJson = line[6..];
                                using var chunk = JsonDocument.Parse(chunkJson);
                                var cr = chunk.RootElement;

                                string? content = null;
                                string? finishReason = null;

                                if (cr.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                                {
                                    var delta = choices[0].TryGetProperty("delta", out var d) ? d : default;
                                    if (delta.ValueKind != JsonValueKind.Undefined)
                                    {
                                        content = delta.TryGetProperty("content", out var c) ? c.GetString() : null;
                                    }
                                    finishReason = choices[0].TryGetProperty("finish_reason", out var fr)
                                        && fr.ValueKind == JsonValueKind.String
                                        ? fr.GetString()
                                        : null;
                                }

                                // Build Ollama streaming chunk
                                using var ms = new MemoryStream();
                                using var w = new Utf8JsonWriter(ms);

                                w.WriteStartObject();
                                w.WriteString("model", model ?? "unknown");
                                w.WriteString("created_at", DateTime.UtcNow.ToString("o"));

                                w.WriteStartObject("message");
                                w.WriteString("role", "assistant");
                                w.WriteString("content", content ?? "");
                                w.WriteEndObject();

                                if (!string.IsNullOrEmpty(finishReason))
                                {
                                    w.WriteString("done_reason", finishReason == "stop" ? "stop" : finishReason);
                                    w.WriteBoolean("done", true);
                                }
                                else
                                {
                                    w.WriteBoolean("done", false);
                                }
                                w.WriteEndObject();
                                w.Flush();

                                var chunkBytes = ms.ToArray();
                                await context.Response.OutputStream.WriteAsync(chunkBytes);
                                await context.Response.OutputStream.WriteAsync("\n"u8.ToArray());
                                await context.Response.OutputStream.FlushAsync();
                            }
                            catch { /* skip malformed chunks */ }
                        }
                    }
                }
                else
                {
                    // Non-streaming: transform full DeepSeek response → Ollama format
                    var responseBody = await forwardResponse.Content.ReadAsStringAsync();

                    if (!forwardResponse.IsSuccessStatusCode)
                    {
                        var errorBytes = Encoding.UTF8.GetBytes(responseBody);
                        await context.Response.OutputStream.WriteAsync(errorBytes);
                        return;
                    }

                    using var dsDoc = JsonDocument.Parse(responseBody);
                    var dsRoot = dsDoc.RootElement;

                    string? content = null;
                    string? finishReason = null;
                    var promptTokens = 0;
                    var completionTokens = 0;

                    if (dsRoot.TryGetProperty("choices", out var dsChoices) && dsChoices.GetArrayLength() > 0)
                    {
                        var choice = dsChoices[0];
                        if (choice.TryGetProperty("message", out var msg))
                        {
                            content = msg.TryGetProperty("content", out var c) ? c.GetString() : null;
                        }
                        finishReason = choice.TryGetProperty("finish_reason", out var fr)
                            ? fr.GetString()
                            : null;
                    }

                    if (dsRoot.TryGetProperty("usage", out var usage))
                    {
                        promptTokens = usage.TryGetProperty("prompt_tokens", out var pt) ? pt.GetInt32() : 0;
                        completionTokens = usage.TryGetProperty("completion_tokens", out var ct) ? ct.GetInt32() : 0;
                    }

                    // Build Ollama response
                    using var outMs = new MemoryStream();
                    using var w = new Utf8JsonWriter(outMs);

                    w.WriteStartObject();
                    w.WriteString("model", model ?? "unknown");
                    w.WriteString("created_at", DateTime.UtcNow.ToString("o"));

                    w.WriteStartObject("message");
                    w.WriteString("role", "assistant");
                    w.WriteString("content", content ?? "");
                    w.WriteEndObject();

                    w.WriteString("done_reason", finishReason == "stop" ? "stop" : finishReason ?? "stop");
                    w.WriteBoolean("done", true);

                    w.WriteNumber("prompt_eval_count", promptTokens);
                    w.WriteNumber("eval_count", completionTokens);
                    w.WriteEndObject();
                    w.Flush();

                    var bytes = outMs.ToArray();
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                }
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
