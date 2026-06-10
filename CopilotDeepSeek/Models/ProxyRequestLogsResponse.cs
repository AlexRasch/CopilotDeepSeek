using System.Text.Json.Serialization;

namespace CopilotDeepSeek.Models;

/// <summary>
/// Response wrapper for proxy request logs with pagination support.
/// </summary>
/// <remarks>
/// <para>
///   <see cref="CurrentPage"/> is 0-based (first page = 0). Display on the frontend by adding 1.
/// </para>
/// <para>
///   <see cref="TotalPages"/> is 0 when there are no matching records, otherwise at least 1.
/// </para>
/// <para>
///   <see cref="PageSize"/> reflects the actual <c>amount</c> query parameter used in the request (capped at 1000).
/// </para>
/// </remarks>
public sealed record ProxyRequestLogsResponse
{
    /// <summary>
    /// Total number of records matching the current filters (ignoring pagination).
    /// </summary>
    [JsonPropertyName("totalCount")]
    public int TotalCount { get; init; }

    /// <summary>
    /// The log entries for the current page.
    /// </summary>
    [JsonPropertyName("logs")]
    public required IEnumerable<CopilotDeepSeek.Database.Entities.ProxyRequest> Logs { get; init; }

    /// <summary>
    /// Current page index, 0-based. First page is 0.
    /// </summary>
    [JsonPropertyName("currentPage")]
    public int CurrentPage { get; init; }

    /// <summary>
    /// Total number of pages. Equals 0 when no records exist.
    /// </summary>
    [JsonPropertyName("totalPages")]
    public int TotalPages { get; init; }

    /// <summary>
    /// Number of entries requested per page (the <c>amount</c> query parameter, capped at 1000).
    /// </summary>
    [JsonPropertyName("pageSize")]
    public int PageSize { get; init; }
}