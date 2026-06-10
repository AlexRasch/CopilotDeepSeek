using System.Text.Json.Serialization;

namespace CopilotDeepSeek.Models;

/// <summary>
/// Aggregated proxy request statistics returned by the /api/requests/stats endpoint.
/// </summary>
public sealed record ProxyStats
{
    [JsonPropertyName("totalRequests")]
    public required int TotalRequests { get; init; }

    [JsonPropertyName("successfulRequests")]
    public required int SuccessfulRequests { get; init; }

    [JsonPropertyName("failedRequests")]
    public required int FailedRequests { get; init; }

    [JsonPropertyName("averageElapsedMs")]
    public required double AverageElapsedMs { get; init; }
}