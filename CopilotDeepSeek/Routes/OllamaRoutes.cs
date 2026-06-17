using CopilotDeepSeek.Constants;
using CopilotDeepSeek.Models;
using CopilotDeepSeek.Utils;
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
                // When the User-Agent is empty (e.g., Visual Studio), return an empty model list
                // to avoid no tool support assumptions and low context windows. D: 
                var userAgent = context.Request.Headers["User-Agent"];
                if (string.IsNullOrEmpty(userAgent))
                {
                    var emptyBytes = JsonSerializer.SerializeToUtf8Bytes(
                        new OllamaTagsResponse { Models = [] },
                        AppJsonContext.Default.OllamaTagsResponse);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    context.Response.ContentLength64 = emptyBytes.Length;
                    await context.Response.OutputStream.WriteAsync(emptyBytes);
                    return;
                }


                using var upstreamResponse = await ctx.GetAsync(
                            ctx.GetEndpointUrl(Constants.ApiEndpoints.Models),
                            useBearerToken: true);

                if (!upstreamResponse.IsSuccessStatusCode)
                {
                    context.Response.StatusCode = (int)upstreamResponse.StatusCode;
                    return;
                }

                var json = await upstreamResponse.Content.ReadAsStringAsync();
                var response = BuildOllamaTagsResponse(json);

                var bytes = JsonSerializer.SerializeToUtf8Bytes(
                    response,
                    AppJsonContext.Default.OllamaTagsResponse);

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

                // Inject :max → reasoning_effort: max
                var modifiedBody = ReasoningEffortInjector.Inject(body);

                // Forward the MODIFIED body to DeepSeek
                using var forwardResponse = await ctx.SendForwardRequestAsync(
                    ctx.GetEndpointUrl(ApiEndpoints.ChatCompletions),
                    modifiedBody,
                    stream);

                context.Response.StatusCode = (int)forwardResponse.StatusCode;
                context.Response.ContentType = ContentTypes.ApplicationJson;

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


        // --- Helpers ---
        private static OllamaTagsResponse BuildOllamaTagsResponse(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var models = new List<OllamaModelEntry>();

            if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in data.EnumerateArray())
                {
                    var id = model.TryGetProperty("id", out var idProp) ? idProp.GetString() : null;
                    if (string.IsNullOrEmpty(id)) continue;

                    var digest = Convert.ToHexString(
                            System.Security.Cryptography.SHA256.HashData(
                                Encoding.UTF8.GetBytes(id)))
                        .ToLowerInvariant();

                    // Base model
                    models.Add(new OllamaModelEntry
                    {
                        Name = id,
                        Model = id,
                        Digest = $"sha256:{digest}",

                    });
                }
            }

            return new OllamaTagsResponse { Models = models };
        }
    }
}
