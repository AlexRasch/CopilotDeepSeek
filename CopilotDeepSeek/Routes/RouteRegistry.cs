using CopilotDeepSeek.Constants;

namespace CopilotDeepSeek.Routes;

/// <summary>
/// Central registry that returns all application routes.
/// Add new routes here when adding new endpoints.
/// </summary>
public static class RouteRegistry
{
    public static IEnumerable<Route> GetAll()
    {
        // Ollama compatible endpoints
        yield return new Route(ProxyRequestMethod.GET, "/", OllamaRoutes.HandleOllamaHealthCheck);
        yield return new Route(ProxyRequestMethod.POST, "/api/chat", OllamaRoutes.HandleChatAsync);
        yield return new Route(ProxyRequestMethod.GET, "/api/tags", OllamaRoutes.HandleOllamaTagsAsync);


        // LM Studio
        // TODO

        // OpenAI compatible endpoints
        yield return new Route(ProxyRequestMethod.POST, "/v1/chat/completions", OpenAiRoutes.HandleChatCompletionsAsync);


        // Web Portal
        yield return new Route(ProxyRequestMethod.GET, "/web", WebRoutes.HandleIndexRequest);

        // Web Portal – internal API
        yield return new Route(ProxyRequestMethod.GET, "/web-api/models", DeepSeekRoutes.HandleDeepSeekModelsAsync, false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/balance", DeepSeekRoutes.HandleDeepSeekBalanceAsync, false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/start", ProxyRoutes.HandleStart, false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/stop", ProxyRoutes.HandleStop,false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/settings", WebRoutes.HandleWebSettingsReadAsync, false);
        yield return new Route(ProxyRequestMethod.PUT, "/web-api/settings", WebRoutes.HandleWebSettingsSaveAsync, false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/stats", WebRoutes.HandleRequestStatsAsync, false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/log", WebRoutes.HandleRequestLogAsync, false);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/logs", WebRoutes.HandleRequestsLogsAsync, false);
    }
}