using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek;

public class ProxyServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly string _targetBase;
    private readonly string _apiKey;
    private readonly SocketsHttpHandler _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _runTask;

    // Reasoning content cache for multi-turn conversations
    private readonly ConcurrentDictionary<string, string> _reasoningCache = new(StringComparer.Ordinal);
    private long _assistantMsgCounter = 0;

    public bool IsRunning { get; private set; }
    public int Port { get; }

    public ProxyServer(Models.Settings settings)
    {
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
    }

    public void Start()
    {
        _listener.Start();
        IsRunning = true;
        _runTask = Task.Run(() => RunLoopAsync(_cts.Token));
    }

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
        try
        {
            {
                // Route specialized endpoints directly
                switch (context.Request.Url!.AbsolutePath)
                {
                    case "/user/balance":
                        await HandleBalanceAsync(context);
                        return;
                    case "/models":
                        await HandleModelsAsync(context);
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

                    // Check if this is a chat completions request and inject cached reasoning
                    if (targetUrl.Contains("/chat/completions"))
                    {
                        requestBody = ModifyRequestBody(body, out isStreaming);
                    }
                    else
                    {
                        requestBody = body;
                    }

                    forwardRequest.Content = new StringContent(requestBody, Encoding.UTF8,
                        context.Request.ContentType ?? "application/json");
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

                    // Cache reasoning content from non-streaming responses
                    if (forwardResponse.IsSuccessStatusCode && targetUrl.Contains("/chat/completions"))
                    {
                        CacheReasoningFromResponse(responseBody);
                    }

                    var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                    await context.Response.OutputStream.WriteAsync(responseBytes);
                }
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 502;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _handler.Dispose();
        (_listener as IDisposable)?.Dispose();
    }

    private async Task HandleBalanceAsync(HttpListenerContext context)
    {
        try
        {
            using var client = new HttpClient(_handler, disposeHandler: false);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_targetBase}/user/balance");

            // Only send the essential headers DeepSeek expects
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request);

            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.StatusDescription = response.ReasonPhrase;
            context.Response.ContentType = "application/json";

            var body = await response.Content.ReadAsStringAsync();
            var bytes = Encoding.UTF8.GetBytes(body);
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 502;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
            Console.WriteLine($"[Balance] ERROR: {ex.Message}");
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task HandleModelsAsync(HttpListenerContext context)
    {
        try
        {
            using var client = new HttpClient(_handler, disposeHandler: false);
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_targetBase}/models");

            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);
            request.Headers.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await client.SendAsync(request);

            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.StatusDescription = response.ReasonPhrase;
            context.Response.ContentType = "application/json";

            var body = await response.Content.ReadAsStringAsync();
            var bytes = Encoding.UTF8.GetBytes(body);
            await context.Response.OutputStream.WriteAsync(bytes);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 502;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
            Console.WriteLine($"[Models] ERROR: {ex.Message}");
        }
        finally
        {
            context.Response.Close();
        }
    }

    private async Task FetchAndLogBalanceAsync(HttpClient client)
    {
        try
        {
            var resp = await client.GetAsync($"{_targetBase}/user/balance");
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync();
                Console.WriteLine($"  Balance: {json}");
            }
        }
        catch { /* non-critical */ }
    }
}