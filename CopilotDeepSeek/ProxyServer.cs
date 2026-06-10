using CopilotDeepSeek.Constants;
using CopilotDeepSeek.Models;
using CopilotDeepSeek.Services;
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


    /// <summary>
    /// Used by webinterface to allow deep seek requests to be proxied through
    /// </summary>
    public void AllowDeepSeekRequests()
    {
        this.AllowDeepSeek = true;
    }

    /// <summary>
    /// Used by webinterface to deny deep seek requests from being proxied through
    /// </summary>
    public void DenyDeepSeekRequests()
    {
        this.AllowDeepSeek = false;
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
            // Route specialized endpoints directly
            switch (context.Request.Url!.AbsolutePath)
            {
                // OLLAMA
                case "/api/tags":
                    await HandleOllamaTagsAsync(context);
                    CompleteRequest(sw, context, 200, true);
                    return;

                // Extra DeepSeek API endpoints
                case "/user/balance":
                case "/models":
                    await HandleSimpleGetAsync(context, sw, context.Request.Url.AbsolutePath);
                    CompleteRequest(sw, context, 200, true);
                    return;
                // Portal & Ollama health check
                case "/":
                    if (context.Request.Headers["Accept"]?.Contains("application/json") == true)
                    {
                        var healthBytes = """{"status":"ollama is running"}"""u8;
                        context.Response.StatusCode = 200;
                        context.Response.ContentType = "application/json; charset=utf-8";
                        context.Response.ContentLength64 = healthBytes.Length;
                        byte[] messageBytes = healthBytes.ToArray();
                        await context.Response.OutputStream.WriteAsync(messageBytes, 0, messageBytes.Length);
                        return;
                    }
                    await HandleIndexAsync(context, sw);
                    CompleteRequest(sw, context, 200, true);
                    return;
                case "/start":
                    this.AllowDeepSeekRequests();
                    await RespondJsonAsync(context, 200, ApiResponse.DeepSeekEnabled());
                    CompleteRequest(sw, context, 200, true);
                    return;
                case "/stop":
                    this.DenyDeepSeekRequests();
                    await RespondJsonAsync(context, 200, ApiResponse.DeepSeekDisabled());
                    CompleteRequest(sw, context, 200, true);
                    return;
                // Webinterface API endpoints
                case "/web/settings":
                    if(context.Request.HttpMethod == "PUT")
                        await HandleWebSettingsSaveAsync(context);
                    else
                        await HandleWebSettingsReadAsync(context);
                    CompleteRequest(sw, context, 200, true);
                    return;

                // Internal API endpoints
                case "/api/requests/stats":
                    await HandleRequestStatsAsync(context);
                    CompleteRequest(sw, context, 200, true);
                    return;
                case "/api/requests/logs":
                    await HandleRequestsLogsAsync(context);
                    CompleteRequest(sw, context, 200, true);
                    return;
                case "/api/requests/log":
                    await HandleRequestLogAsync(context);
                    CompleteRequest(sw, context, 200, true);
                    return;

                default:
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
                    break;
            }

            // If user have decided to deny deep seek requests, block any non-static requests to the proxy
            if (!AllowDeepSeek)
            {
                //context.Response.StatusCode = 403;
                await RespondJsonAsync(context, 403, ApiResponse.ErrorResponse("Proxy requests to DeepSeek are disabled"));
                CompleteRequest(sw, context, 403, false);
                //context.Response.Close();
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

                forwardRequest.Content = new StringContent(requestBody, Encoding.UTF8,context.Request.ContentType ?? "application/json");
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

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _handler.Dispose();
        (_listener as IDisposable)?.Dispose();
    }

    private async Task HandleIndexAsync(HttpListenerContext context, Stopwatch sw)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "www", "index.html");

            if (!File.Exists(path))
            {
                context.Response.StatusCode = 404;
                CompleteRequest(sw, context, 404, false);
                return;
            }

            var bytes = await File.ReadAllBytesAsync(path);
            context.Response.StatusCode = 200;
            context.Response.ContentType = "text/html; charset=utf-8";
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

    private async Task HandleSimpleGetAsync(HttpListenerContext context, Stopwatch sw, string path)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_targetBase}{path}");

            var bytes = await response.Content.ReadAsByteArrayAsync();

            context.Response.StatusCode = (int)response.StatusCode;
            context.Response.StatusDescription = response.ReasonPhrase ?? string.Empty;
            context.Response.ContentType = "application/json";
            context.Response.ContentLength64 = bytes.Length;

            await context.Response.OutputStream.WriteAsync(bytes);
            CompleteRequest(sw, context, (int)response.StatusCode, response.IsSuccessStatusCode);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 502;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
            CompleteRequest(sw, context, 502, false);
        }
        finally
        {
            context.Response.Close();
        }
    }

    private static async Task RespondJsonAsync(HttpListenerContext context, int statusCode, ApiResponse response)
    {
        var json = JsonSerializer.Serialize(response, AppJsonContext.Default.ApiResponse);
        var bytes = Encoding.UTF8.GetBytes(json);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    private async Task HandleRequestStatsAsync(HttpListenerContext context)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

            var stats = await dbContext.ProxyRequests
                .GroupBy(r => 1)
                .Select(g => new ProxyStats
                {
                    TotalRequests = g.Count(),
                    SuccessfulRequests = g.Count(r => r.IsSuccess),
                    FailedRequests = g.Count(r => !r.IsSuccess),
                    AverageElapsedMs = g.Average(r => r.ElapsedMs)
                })
                .FirstOrDefaultAsync();

            if (stats == null)
            {
                JsonSerializer.SerializeToElement(
                    new ProxyStats
                    {
                        TotalRequests = 0,
                        SuccessfulRequests = 0,
                        FailedRequests = 0,
                        AverageElapsedMs = 0
                    },
                AppJsonContext.Default.ProxyStats);
            }

            await RespondJsonAsync(context, 200, ApiResponse.OkWithData(JsonSerializer.SerializeToElement(stats, AppJsonContext.Default.ProxyStats)));
        }
        catch (Exception ex)
        {
            await RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
        }
    }

    private async Task HandleRequestsLogsAsync(HttpListenerContext context)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

            var query = context.Request.Url?.Query;
            DateTime? fromDate = null;
            DateTime? toDate = null;
            var page = 0;
            var amount = 50;

            if (!string.IsNullOrEmpty(query))
            {
                var queryParams = System.Web.HttpUtility.ParseQueryString(query);

                if (DateTime.TryParse(queryParams["from"], out var parsedFrom))
                    fromDate = parsedFrom;

                if (DateTime.TryParse(queryParams["to"], out var parsedTo))
                    toDate = parsedTo;

                if (int.TryParse(queryParams["page"], out var parsedPage) && parsedPage >= 0)
                    page = parsedPage;

                if (int.TryParse(queryParams["amount"], out var parsedAmount) && parsedAmount > 0)
                    amount = Math.Min(parsedAmount, 1000);
            }

            var logsQuery = dbContext.ProxyRequests.AsQueryable();

            if (fromDate.HasValue)
                logsQuery = logsQuery.Where(r => r.Timestamp >= fromDate.Value);

            if (toDate.HasValue)
                logsQuery = logsQuery.Where(r => r.Timestamp < toDate.Value.AddDays(1));

            var totalCount = await logsQuery.CountAsync();
            var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / amount) : 0;

            // Clamp page to valid range
            if (totalPages > 0 && page >= totalPages)
                page = totalPages - 1;
            else if (page < 0)
                page = 0;


            var logs = await logsQuery
                .OrderByDescending(r => r.Timestamp)
                .Skip(page * amount)
                .Take(amount)
                .ToListAsync();

            var response = new ProxyRequestLogsResponse
            {
                TotalCount = totalCount,
                Logs = logs,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = amount
            };

            await RespondJsonAsync(context, 200, ApiResponse.OkWithData(JsonSerializer.SerializeToElement(response, AppJsonContext.Default.ProxyRequestLogsResponse)));
        }
        catch (Exception ex)
        {
            await RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
        }
    }

    private async Task HandleRequestLogAsync(HttpListenerContext context)
    {
        try
        {
            var query = context.Request.Url?.Query;
            if (string.IsNullOrEmpty(query))
            {
                await RespondJsonAsync(context, 400, ApiResponse.ErrorResponse("Missing 'id' query parameter."));
                return;
            }

            var queryParams = System.Web.HttpUtility.ParseQueryString(query);
            var idStr = queryParams["id"];

            if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
            {
                await RespondJsonAsync(context, 400, ApiResponse.ErrorResponse("Invalid or missing 'id' query parameter."));
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

            var logEntry = await dbContext.ProxyRequests.FindAsync(id);

            if (logEntry is null)
            {
                await RespondJsonAsync(context, 404, ApiResponse.ErrorResponse("Log entry not found."));
                return;
            }

            await RespondJsonAsync(context, 200, ApiResponse.OkWithData(
                JsonSerializer.SerializeToElement(logEntry, AppJsonContext.Default.ProxyRequest)));
        }
        catch (Exception ex)
        {
            await RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
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


    // Ollama

    /// <summary>
    /// Fetches the real model list from DeepSeek's /models endpoint and
    /// transforms it into an Ollama-compatible /api/tags response.
    /// </summary>
    private async Task HandleOllamaTagsAsync(HttpListenerContext context)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{_targetBase}/models");

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

                    var modelTag = $"{id}:latest";
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


    private async Task HandleWebSettingsReadAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod != "GET")
            {
                await RespondJsonAsync(context, 405, ApiResponse.ErrorResponse("Method not allowed. Use GET."));
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            var settings = settingsService.LoadForWebInterface();

            await RespondJsonAsync(context, 200, 
                ApiResponse.OkWithData(JsonSerializer.SerializeToElement(settings, AppJsonContext.Default.Settings)));
        }
        catch (Exception ex)
        {
            await RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
        }
    }

    private async Task HandleWebSettingsSaveAsync(HttpListenerContext context)
    {
        try
        {
            if (context.Request.HttpMethod != "PUT")
            {
                await RespondJsonAsync(context, 405, ApiResponse.ErrorResponse("Method not allowed. Use PUT."));
                return;
            }

            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();

            var settings = JsonSerializer.Deserialize<Settings>(body, AppJsonContext.Default.Settings);
            if(settings is null)
            {
                await RespondJsonAsync(context, 400, ApiResponse.ErrorResponse("Invalid JSON body."));
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
            settingsService.Save(settings);

            await RespondJsonAsync(
                context,
                200,
                ApiResponse.OkWithData(JsonSerializer.SerializeToElement(
                    new ApiResponse { },
                    AppJsonContext.Default.ApiResponse)
                ));
        }
        catch (Exception ex)
        {
            await RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
        }
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