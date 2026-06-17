using System.Text.Json.Serialization;

namespace CopilotDeepSeek.Models;

/// <summary>
/// Simplified response model for Ollama's GET /api/tags format.
/// Only contains fields needed by Visual Studio's Copilot addon
/// for displaying model names (including :max variants).
/// </summary>
public sealed record OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public required List<OllamaModelEntry> Models { get; init; }
}

public sealed record OllamaModelEntry
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("digest")]
    public required string Digest { get; init; }

}