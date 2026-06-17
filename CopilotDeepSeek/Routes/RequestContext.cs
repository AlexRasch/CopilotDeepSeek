using CopilotDeepSeek.Models;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Routes;

public delegate Task RouteHandler(HttpListenerContext context, RequestContext ctx);

/// <summary>
/// Shared context passed to every route handler.
/// Carries all services and state that handlers need from ProxyServer.
/// </summary>
public sealed class RequestContext
{
    // Injected services
    public IServiceScopeFactory ScopeFactory { get; }
    public HttpClient HttpClient { get; }
    public string TargetBase { get; }
    public string ApiKey { get; }
    public SocketsHttpHandler SocketHandler { get; }

    // Shared mutable state
    public ConcurrentDictionary<string, string> ReasoningCache { get; }
    public long AssistantMsgCounter;
    public bool AllowDeepSeek { get; set; } = true;

    public RequestContext(
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        string targetBase,
        string apiKey,
        SocketsHttpHandler socketHandler,
        ConcurrentDictionary<string, string> reasoningCache)
    {
        ScopeFactory = scopeFactory;
        HttpClient = httpClient;
        TargetBase = targetBase;
        ApiKey = apiKey;
        SocketHandler = socketHandler;
        ReasoningCache = reasoningCache;
    }

    public static async Task RespondJsonAsync(HttpListenerContext context, int statusCode, ApiResponse response)
    {
        var json = JsonSerializer.Serialize(response, AppJsonContext.Default.ApiResponse);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    public async Task HandleSimpleGetAsync(HttpListenerContext context, string url)
    {
        try
        {
            using var response = await HttpClient.GetAsync(url);

            var bytes = await response.Content.ReadAsByteArrayAsync();

            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.StatusDescription = response.ReasonPhrase ?? string.Empty;
            context.Response.ContentType = "application/json";
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

    public async Task DeepSeekDeniedResponseAsync(HttpListenerContext context)
    {
        await RespondJsonAsync(context, 403, ApiResponse.ErrorResponse("Proxy requests to DeepSeek are disabled"));
    }

    // DeepSeek Chat logic 

    /// <summary>
    /// Injects cached reasoning_content into assistant messages in a chat completions request body.
    /// </summary>
    public string ModifyRequestBody(string body, out bool isStreaming)
    {
        isStreaming = false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (root.TryGetProperty("stream", out var sp))
                isStreaming = sp.GetBoolean();

            if (!root.TryGetProperty("messages", out var msgs))
                return body;

            int idx = 0;
            bool modified = false;
            using var ms = new MemoryStream();
            using var w = new Utf8JsonWriter(ms);

            w.WriteStartObject();
            foreach (var prop in root.EnumerateObject())
            {
                if (!prop.NameEquals("messages"))
                {
                    prop.WriteTo(w);
                    continue;
                }

                w.WritePropertyName("messages");
                w.WriteStartArray();

                foreach (var msg in msgs.EnumerateArray())
                {
                    var role = msg.TryGetProperty("role", out var r) ? r.GetString() : null;

                    if (role == "assistant")
                    {
                        bool hasTc = msg.TryGetProperty("tool_calls", out var tcArr) && tcArr.GetArrayLength() > 0;
                        string? key = null;

                        if (hasTc)
                        {
                            var ids = new List<string>();
                            foreach (var tc in tcArr.EnumerateArray())
                                if (tc.TryGetProperty("id", out var idE) && idE.ValueKind == JsonValueKind.String)
                                    ids.Add(idE.GetString()!);
                            if (ids.Count > 0) key = $"toolcall:{string.Join("|", ids)}";
                        }
                        else
                        {
                            key = $"assistant:{idx++}";
                        }

                        if (key != null && ReasoningCache.TryGetValue(key, out var rc))
                        {
                            bool needsInject = !msg.TryGetProperty("reasoning_content", out var exRc)
                                || exRc.ValueKind != JsonValueKind.String
                                || string.IsNullOrEmpty(exRc.GetString());

                            if (needsInject)
                            {
                                w.WriteStartObject();
                                foreach (var mp in msg.EnumerateObject())
                                    mp.WriteTo(w);
                                w.WriteString("reasoning_content", rc);
                                w.WriteEndObject();
                                modified = true;
                                continue;
                            }
                        }
                    }

                    msg.WriteTo(w);
                }

                w.WriteEndArray();
            }
            w.WriteEndObject();
            w.Flush();

            return modified ? Encoding.UTF8.GetString(ms.ToArray()) : body;
        }
        catch
        {
            return body;
        }
    }

    /// <summary>
    /// Caches reasoning_content from a non-streaming chat completion response.
    /// </summary>
    public void CacheReasoningFromResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                return;

            var msg = choices[0].TryGetProperty("message", out var m) ? m
                : choices[0].TryGetProperty("delta", out var d) ? d : default;

            if (msg.ValueKind == JsonValueKind.Undefined)
                return;

            if (!msg.TryGetProperty("reasoning_content", out var rc) || string.IsNullOrEmpty(rc.GetString()))
                return;

            string key;
            if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.GetArrayLength() > 0)
            {
                var ids = new List<string>();
                foreach (var tc in tcs.EnumerateArray())
                    if (tc.TryGetProperty("id", out var idE) && idE.ValueKind == JsonValueKind.String)
                        ids.Add(idE.GetString()!);
                key = $"toolcall:{string.Join("|", ids)}";
            }
            else
            {
                key = $"assistant:{Interlocked.Increment(ref AssistantMsgCounter) - 1}";
            }

            ReasoningCache[key] = rc.GetString()!;
        }
        catch { /* cache errors are non-critical */ }
    }

    /// <summary>
    /// Streams SSE chunks from upstream to downstream while caching reasoning_content.
    /// Used by the OpenAI-compatible /v1/chat/completions route.
    /// </summary>
    public async Task StreamAndCacheResponseAsync(HttpResponseMessage upstream, HttpListenerResponse downstream)
    {
        using var upstreamStream = await upstream.Content.ReadAsStreamAsync();
        using var reader = new StreamReader(upstreamStream);

        var sb = new StringBuilder(4096);
        List<string>? tcIds = null;
        bool hasTc = false;
        int? asstIdx = null;

        while (true)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            if (line.StartsWith("data:"))
            {
                var json = line.Substring(5).TrimStart();

                if (json.Length > 0 && json != "[DONE]")
                {
                    try
                    {
                        using var chunk = JsonDocument.Parse(json);
                        var cr = chunk.RootElement;

                        if (cr.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                        {
                            var delta = choices[0].TryGetProperty("delta", out var d) ? d
                                : choices[0].TryGetProperty("message", out var mm) ? mm : default;

                            if (delta.ValueKind != JsonValueKind.Undefined)
                            {
                                if (delta.TryGetProperty("reasoning_content", out var rc)
                                    && rc.ValueKind == JsonValueKind.String)
                                {
                                    var rct = rc.GetString();
                                    if (!string.IsNullOrEmpty(rct))
                                        sb.Append(rct);
                                }

                                if (delta.TryGetProperty("tool_calls", out var tcs)
                                    && tcs.ValueKind == JsonValueKind.Array)
                                {
                                    hasTc = true;
                                    foreach (var tc in tcs.EnumerateArray())
                                    {
                                        if (tc.TryGetProperty("id", out var idE)
                                            && idE.ValueKind == JsonValueKind.String)
                                        {
                                            tcIds ??= new List<string>();
                                            var id = idE.GetString()!;
                                            if (!tcIds.Contains(id)) tcIds.Add(id);
                                        }
                                    }
                                }

                                if (choices[0].TryGetProperty("finish_reason", out var fr)
                                    && fr.ValueKind != JsonValueKind.Null)
                                {
                                    var reasoning = sb.ToString();
                                    if (!string.IsNullOrEmpty(reasoning))
                                    {
                                        string key;
                                        if (hasTc && tcIds != null && tcIds.Count > 0)
                                            key = $"toolcall:{string.Join("|", tcIds)}";
                                        else
                                            key = $"assistant:{asstIdx ?? (int)(Interlocked.Increment(ref AssistantMsgCounter) - 1)}";

                                        ReasoningCache[key] = reasoning;
                                    }
                                }
                            }
                        }
                    }
                    catch { /* parse errors are non-critical */ }
                }
            }

            // Pass through all lines unmodified
            var lineBytes = Encoding.UTF8.GetBytes(line + "\n");
            await downstream.OutputStream.WriteAsync(lineBytes);
            await downstream.OutputStream.FlushAsync();
        }
    }

    // Helper 

    /// <summary>
    /// Sets the Bearer token authorization header on a forwarded request.
    /// </summary>
    public void SetBearerTokenAuthHeader(HttpRequestMessage request)
    {
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ApiKey);
    }

    // Debugging methods

    [Conditional("DEBUG")]
    public static void DumpJson(string label, string json, int maxLength = 10000)
    {
        var truncated = json.Length > maxLength
            ? json[..maxLength] + $"\n... (truncated, {json.Length} chars total)"
            : json;
        Console.Error.WriteLine($"[PROXY] {label}:");
        Console.Error.WriteLine(truncated);
        Console.Error.WriteLine();
    }
}