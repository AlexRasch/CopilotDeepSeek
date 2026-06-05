using System.Text.Json.Serialization;

namespace CopilotDeepSeek.Models;

/// <summary>
/// Response wrapper for proxy request logs that includes total count.
/// </summary>
public sealed record ProxyRequestLogsResponse
{
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    [JsonPropertyName("logs")]
    public required IEnumerable<CopilotDeepSeek.Database.Entities.ProxyRequest> Logs { get; init; }
}