using CopilotDeepSeek.Constants;

namespace CopilotDeepSeek.Utils;

/// <summary>
/// High-performance parser for HTTP method strings to ProxyRequestMethod enum.
/// </summary>
public static class HttpMethodParser
{
    /// <summary>
    /// Parses an HTTP method string to ProxyRequestMethod.
    /// </summary>
    public static ProxyRequestMethod Parse(string method) => method switch
    {
        "GET" => ProxyRequestMethod.GET,
        "HEAD" => ProxyRequestMethod.HEAD,
        "OPTIONS" => ProxyRequestMethod.OPTIONS,
        "TRACE" => ProxyRequestMethod.TRACE,
        "PUT" => ProxyRequestMethod.PUT,
        "DELETE" => ProxyRequestMethod.DELETE,
        "POST" => ProxyRequestMethod.POST,
        "PATCH" => ProxyRequestMethod.PATCH,
        "CONNECT" => ProxyRequestMethod.CONNECT,
        _ => throw new ArgumentException($"Unknown HTTP method: '{method}'", nameof(method))
    };

    /// <summary>
    /// Tries to parse an HTTP method string without throwing.
    /// Returns false for unknown methods.
    /// </summary>
    public static bool TryParse(string method, out ProxyRequestMethod result)
    {
        var parsed = method switch
        {
            "GET" => ProxyRequestMethod.GET,
            "HEAD" => ProxyRequestMethod.HEAD,
            "OPTIONS" => ProxyRequestMethod.OPTIONS,
            "TRACE" => ProxyRequestMethod.TRACE,
            "PUT" => ProxyRequestMethod.PUT,
            "DELETE" => ProxyRequestMethod.DELETE,
            "POST" => ProxyRequestMethod.POST,
            "PATCH" => ProxyRequestMethod.PATCH,
            "CONNECT" => ProxyRequestMethod.CONNECT,
            _ => (ProxyRequestMethod)byte.MaxValue // sentinel
        };

        result = parsed;
        return parsed != (ProxyRequestMethod)byte.MaxValue;
    }
}