using System.Text.Json;
using System.Text.Json.Serialization;

namespace CopilotDeepSeek.Models;

/// <summary>
/// Standardized API response for control endpoints.
/// </summary>
public sealed record ApiResponse
{
    /// <summary>
    /// Human-readable message describing the result.
    /// </summary>
    [JsonPropertyName("message")]
    public string Message { get; init; } = string.Empty;

    /// <summary>
    /// Numeric status code specific to this API:
    /// 0 = DeepSeek requests disabled (stopped)
    /// 1 = DeepSeek requests enabled (running)
    /// -1 = Error
    /// </summary>
    [JsonPropertyName("status")]
    public int Status { get; init; }

    /// <summary>
    /// Optional error details when status is -1.
    /// </summary>
    [JsonPropertyName("error")]
    public string? Error { get; init; }

    /// <summary>
    /// Optional payload for responses that carry data (e.g. stats, lists).
    /// Serialized as a raw JSON object — use <c>JsonSerializer.SerializeToElement()</c> to populate.
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? Data { get; init; }

    public static ApiResponse DeepSeekEnabled() =>
        new() { Message = "DeepSeek proxy requests are now enabled", Status = 1 };

    public static ApiResponse DeepSeekDisabled() =>
        new() { Message = "DeepSeek proxy requests are now disabled", Status = 0 };

    public static ApiResponse ErrorResponse(string error) =>
        new() { Message = "An error occurred", Status = -1, Error = error };

    public static ApiResponse OkWithData(JsonElement data) =>
        new() { Message = "Success", Status = 1, Data = data };
}