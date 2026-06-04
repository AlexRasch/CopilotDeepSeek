namespace CopilotDeepSeek.Database.Entities;

/// <summary>
/// Entity representing a logged proxy request.
/// </summary>
public class ProxyRequest
{
    /// <summary>
    /// Primary key - UUID v7 (time-ordered GUID).
    /// </summary>
    public Guid Id { get; set; }

    public required string Method { get; set; }
    public required string Path { get; set; }
    public int StatusCode { get; set; }
    public TimeSpan Elapsed { get; set; }
    public bool IsSuccess { get; set; }
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp when the request was logged.
    /// </summary>
    public DateTime Timestamp { get; set; }
}