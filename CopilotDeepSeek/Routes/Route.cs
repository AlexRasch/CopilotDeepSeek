using CopilotDeepSeek.Constants;

namespace CopilotDeepSeek.Routes;

/// <summary>
/// A single route entry: HTTP method, path, and handler delegate.
/// </summary>
public sealed record Route(ProxyRequestMethod Method, string Path, RouteHandler Handler, bool RequiresActiveProxy = true);