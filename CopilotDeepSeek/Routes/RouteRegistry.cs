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
        //yield return new Route("GET", "/api/tags", OllamaRoutes.HandleTags);
        //yield return new Route("POST", "/api/chat", OllamaRoutes.HandleChat);

        // LM Studio 

        // OpenAI compatible endpoints


        // Web Portal – internal API
        yield return new Route(ProxyRequestMethod.GET, "/web-api/start", ProxyRoutes.HandleStart);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/stop", ProxyRoutes.HandleStop);
        yield return new Route(ProxyRequestMethod.GET, "/web-api/settings", WebRoutes.HandleWebSettingsReadAsync);
        yield return new Route(ProxyRequestMethod.PUT, "/web-api/settings", WebRoutes.HandleWebSettingsSaveAsync);


        // Web portal & settings
        //yield return new Route("GET", "/", WebRoutes.HandleIndex);
        //
        //// Internal API
        //yield return new Route("GET", "/api/requests/stats", ApiRoutes.HandleStats);
        //yield return new Route("GET", "/api/requests/logs", ApiRoutes.HandleLogs);
        //yield return new Route("GET", "/api/requests/log", ApiRoutes.HandleLog);

        // DeepSeek API passthrough
        //yield return new Route("GET", "/user/balance", ProxyRoutes.HandleSimpleGet);
        //yield return new Route("GET", "/models", ProxyRoutes.HandleSimpleGet);
    }
}