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
    // -- Injected services --
    public IServiceScopeFactory ScopeFactory { get; }
    public HttpClient HttpClient { get; }
    public string TargetBase { get; }
    public string ApiKey { get; }
    public SocketsHttpHandler SocketHandler { get; }

    // -- Shared mutable state --
    public ConcurrentDictionary<string, string> ReasoningCache { get; }
    public long AssistantMsgCounter { get; set; }
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