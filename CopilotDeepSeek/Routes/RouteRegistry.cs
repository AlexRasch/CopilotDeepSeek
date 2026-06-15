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
        // Ollama-compatible endpoints
        yield return new Route(ProxyRequestMethod.GET, "/api/tags", OllamaRoutes.HandleOllamaTagsAsync);
        //yield return new Route("POST", "/api/chat", OllamaRoutes.HandleChat);

        // LM Studio 

        // OpenAI compatible endpoints


        // Web Portal – internal API
        yield return new Route(ProxyRequestMethod.GET, "/web-api/models", DeepSeekRoutes.HandleDeepSeekModelsAsync);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/balance", DeepSeekRoutes.HandleDeepSeekBalanceAsync);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/start", ProxyRoutes.HandleStart);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/stop", ProxyRoutes.HandleStop);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/settings", WebRoutes.HandleWebSettingsReadAsync);
        yield return new Route(ProxyRequestMethod.PUT, "/web-api/settings", WebRoutes.HandleWebSettingsSaveAsync);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/stats", WebRoutes.HandleRequestStatsAsync);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/log", WebRoutes.HandleRequestLogAsync);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/logs", WebRoutes.HandleRequestsLogsAsync);


        // Web portal & settings
        //yield return new Route("GET", "/", WebRoutes.HandleIndex);
        // DeepSeek API passthrough
        //yield return new Route("GET", "/user/balance", ProxyRoutes.HandleSimpleGet);
        //yield return new Route("GET", "/models", ProxyRoutes.HandleSimpleGet);
    }
}