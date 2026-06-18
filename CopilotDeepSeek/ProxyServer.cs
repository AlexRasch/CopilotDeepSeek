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
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue(ContentTypes.ApplicationJson));

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