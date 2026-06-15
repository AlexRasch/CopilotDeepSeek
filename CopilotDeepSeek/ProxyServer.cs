using CopilotDeepSeek.Constants;
using CopilotDeepSeek.Models;
using CopilotDeepSeek.Routes;
using CopilotDeepSeek.Services;
using CopilotDeepSeek.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek;

public class ProxyServer : IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;

    private readonly HttpListener _listener;
    private readonly string _targetBase;
    private readonly string _apiKey;
    private readonly SocketsHttpHandler _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _runTask;

    private readonly HttpClient _httpClient;

    private readonly List<Route> _routes = new();
    private readonly RequestContext _requestContext;


    // Reasoning content cache for multi-turn conversations
    private readonly ConcurrentDictionary<string, string> _reasoningCache = new(StringComparer.Ordinal);
    private long _assistantMsgCounter = 0;

    public bool IsRunning { get; private set; }

    public bool AllowDeepSeek { get; private set; } = true;

    public int Port { get; }

    /// <summary>
    /// Raised on the thread-pool thread that handled the request,
    /// after the response has been fully sent to the client.
    /// Subscribers must be thread-safe.
    /// </summary>
    public event Action<ProxyRequestEvent>? RequestCompleted;

    private void RaiseRequestCompleted(ProxyRequestEvent evt) => RequestCompleted?.Invoke(evt);

    private void CompleteRequest(Stopwatch sw, HttpListenerContext context, int statusCode, bool isSuccess)
    {
        sw.Stop();
        RaiseRequestCompleted(new ProxyRequestEvent(
            context.Request.HttpMethod,
            context.Request.Url?.AbsolutePath ?? "/",
            statusCode,
            sw.Elapsed,
            isSuccess));
    }

    public ProxyServer(Models.Settings settings, IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;

        Port = settings.Port;
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://localhost:{settings.Port}/");
        _targetBase = settings.BaseUrl.TrimEnd('/');
        _apiKey = SecurityHelper.Decrypt(settings.ApiKey);

        _handler = new SocketsHttpHandler
        {
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 256,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            KeepAlivePingDelay = TimeSpan.FromSeconds(30),
            KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
            UseCookies = false,
            PreAuthenticate = false
        };

        _httpClient = new HttpClient(_handler, disposeHandler: false);
        _httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

        _requestContext = new RequestContext(_scopeFactory, _httpClient, _targetBase, _apiKey, _handler, _reasoningCache);
        _routes.AddRange(RouteRegistry.GetAll());
    }

    /// <summary>
    /// Used by CLI to Start the entire proxy server.
    /// Returns true if the listener was started successfully, false if the port is already in use.
    /// </summary>
    public bool Start()
    {
        try
        {
            _listener.Start();
            IsRunning = true;
            _runTask = Task.Run(() => RunLoopAsync(_cts.Token));
            return true;
        }
        catch (HttpListenerException ex) when (ex.ErrorCode == 183) // ERROR_ALREADY_EXISTS
        {
            IsRunning = false;
            Console.Error.WriteLine($"Port {Port} is already in use by another process.");
            return false;
        }
        catch (HttpListenerException ex)
        {
            IsRunning = false;
            Console.Error.WriteLine($"Failed to start HTTP listener on port {Port}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Used by CLI to Stop the entire proxy server.
    /// </summary>
    public void Stop()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* already stopped */ }
        IsRunning = false;
    }

    private async Task RunLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var context = await _listener.GetContextAsync().WaitAsync(token);
                _ = HandleRequestAsync(context);
            }
            catch (OperationCanceledException) { break; }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var method = HttpMethodParser.Parse(context.Request.HttpMethod);
            var path = context.Request.Url!.AbsolutePath;

            var route = _routes.FirstOrDefault(r => r.Method == method && r.Path == path);
            if (route is not null)
            {
                if (AllowDeepSeek)
                    await route.Handler(context, _requestContext);
                else
                    await _requestContext.DeepSeekDeniedResponseAsync(context);
                CompleteRequest(sw, context, context.Response.StatusCode, context.Response.StatusCode < 400);
                return;
            }

            // Serve static files from www/ for GET requests with known extensions
            if (context.Request.HttpMethod == "GET")
            {
                var ext = Path.GetExtension(context.Request.Url!.AbsolutePath);
                if (MimeTypes.StaticMimeTypes.ContainsKey(ext))
                {
                    await HandleStaticFileAsync(context, sw);
                    return;
                }
            }

            // Route specialized endpoints directly
            switch (context.Request.Url!.AbsolutePath)
            {
                case "/api/chat":
                    await HandleOllamaChatAsync(context);
                    CompleteRequest(sw, context, 200, true);
                    return;
            }

            // General proxy logic for all other endpoints
            var targetUrl = $"{_targetBase}{context.Request.Url!.AbsolutePath}{context.Request.Url.Query}";

            using var client = new HttpClient(_handler, disposeHandler: false);
            using var forwardRequest = new HttpRequestMessage(
                new HttpMethod(context.Request.HttpMethod), targetUrl);

            // Copy headers (skip Host to avoid conflicts)
            foreach (string? key in context.Request.Headers.AllKeys)
            {
                if (key != null && !string.Equals(key, "Host", StringComparison.OrdinalIgnoreCase))
                {
                    forwardRequest.Headers.TryAddWithoutValidation(key, context.Request.Headers[key]);
                }
            }

            // Set authentication
            forwardRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            // Copy and potentially modify request body
            string? requestBody = null;
            bool isStreaming = false;

            if (context.Request.HasEntityBody)
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();
                DumpJson("INCOMING REQUEST BODY", body);

                // Check if this is a chat completions request and inject cached reasoning
                if (targetUrl.Contains("/chat/completions"))
                {
                    requestBody = ModifyRequestBody(body, out isStreaming);
                }
                else
                {
                    requestBody = body;
                }

                forwardRequest.Content = new StringContent(requestBody, Encoding.UTF8, context.Request.ContentType ?? "application/json");
                DumpJson("FORWARDED REQUEST BODY", requestBody);
            }

            // Forward to DeepSeek
            var completionOption = isStreaming
                ? HttpCompletionOption.ResponseHeadersRead
                : HttpCompletionOption.ResponseContentRead;

            using var forwardResponse = await client.SendAsync(forwardRequest, completionOption);

            // Copy response status and headers
            context.Response.StatusCode = (int)forwardResponse.StatusCode;
            context.Response.StatusDescription = forwardResponse.ReasonPhrase;

            foreach (var header in forwardResponse.Headers)
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);

            foreach (var header in forwardResponse.Content.Headers)
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);

            // Handle streaming vs non-streaming responses
            if (isStreaming && forwardResponse.IsSuccessStatusCode)
            {
                await StreamAndCacheResponse(forwardResponse, context.Response);
            }
            else
            {
                var responseBody = await forwardResponse.Content.ReadAsStringAsync();
                DumpJson("RESPONSE BODY", responseBody);

                // Cache reasoning content from non-streaming responses
                if (forwardResponse.IsSuccessStatusCode && targetUrl.Contains("/chat/completions"))
                {
                    CacheReasoningFromResponse(responseBody);
                }

                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                await context.Response.OutputStream.WriteAsync(responseBytes);
            }

            CompleteRequest(sw, context, (int)forwardResponse.StatusCode, forwardResponse.IsSuccessStatusCode);

        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 502;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
            CompleteRequest(sw, context, context.Response.StatusCode, false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private string? ModifyRequestBody(string body, out bool isStreaming)
    {
        isStreaming = false;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Check if streaming
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

                        if (key != null && _reasoningCache.TryGetValue(key, out var rc))
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

    private void CacheReasoningFromResponse(string json)
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
                key = $"assistant:{Interlocked.Increment(ref _assistantMsgCounter) - 1}";
            }

            _reasoningCache[key] = rc.GetString()!;
        }
        catch { /* cache errors are non-critical */ }
    }

    private async Task StreamAndCacheResponse(HttpResponseMessage upstream, HttpListenerResponse downstream)
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
                                            key = $"assistant:{asstIdx ?? (int)(Interlocked.Increment(ref _assistantMsgCounter) - 1)}";

                                        _reasoningCache[key] = reasoning;
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

    private async Task HandleStaticFileAsync(HttpListenerContext context, Stopwatch sw)
    {
        try
        {
            // Sanitize path to prevent directory traversal
            var relativePath = context.Request.Url!.AbsolutePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "www", relativePath));
            var wwwRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "www"));

            // Ensure the resolved path stays within www/
            if (!fullPath.StartsWith(wwwRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                context.Response.StatusCode = 404;
                CompleteRequest(sw, context, 404, false);
                return;
            }

            var ext = Path.GetExtension(fullPath);
            var bytes = await File.ReadAllBytesAsync(fullPath);
            context.Response.StatusCode = 200;
            context.Response.ContentType = MimeTypes.StaticMimeTypes[ext];
            context.Response.ContentLength64 = bytes.Length;
            await context.Response.OutputStream.WriteAsync(bytes);
            CompleteRequest(sw, context, 200, true);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 500;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
            CompleteRequest(sw, context, 500, false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    /// <summary>
    /// Translates Ollama /api/chat requests to DeepSeek /chat/completions
    /// and transforms responses back to Ollama format.
    /// Supports both streaming and non-streaming.
    /// </summary>
    private async Task HandleOllamaChatAsync(HttpListenerContext context)
    {
        try
        {
            // Read Ollama-format request body
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            DumpJson("OLLAMA INCOMING", body);

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            // Extract model and streaming flag
            var model = root.TryGetProperty("model", out var m) ? m.GetString() : "";
            var stream = root.TryGetProperty("stream", out var s) && s.GetBoolean();

            // Forward to DeepSeek /chat/completions (body format is identical for requests)
            using var client = new HttpClient(_handler, disposeHandler: false);
            using var forwardRequest = new HttpRequestMessage(HttpMethod.Post, $"{_targetBase}/chat/completions")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            forwardRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

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

                var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _handler.Dispose();
        (_listener as IDisposable)?.Dispose();
    }

    [Conditional("DEBUG")]
    private static void DumpJson(string label, string json, int maxLength = 10000)
    {
        var truncated = json.Length > maxLength ? json[..maxLength] + $"\n... (truncated, {json.Length} chars total)" : json;
        Console.Error.WriteLine($"[PROXY] {label}:");
        Console.Error.WriteLine(truncated);
        Console.Error.WriteLine();
    }
}