namespace CopilotDeepSeek.Models;

/// <summary>
/// Snapshot of a single proxy request, raised after the response is sent.
/// </summary>
public sealed record ProxyRequestEvent(
    string Method,
    string Path,
    int StatusCode,
    TimeSpan Elapsed,
    bool IsSuccess,
    string? ErrorMessage = null
);