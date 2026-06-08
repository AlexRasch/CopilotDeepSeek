namespace CopilotDeepSeek.Database.Entities;

/// <summary>
/// Entity representing an AI model available for use.
/// </summary>
public class AiModel
{
    /// <summary>
    /// Primary key - auto-increment.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Model identifier used by the API (e.g. "deepseek-v4-flash").
    /// </summary>
    public required string Name { get; set; }

    /// <summary>
    /// Whether this model is currently allowed for use.
    /// </summary>
    public bool IsAllowed { get; set; } = true;
}
