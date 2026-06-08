namespace CopilotDeepSeek.Database.Entities;

/// <summary>
/// Entity representing application settings (single-row table).
/// Only one row ever exists — Id is always 1.
/// </summary>
public class SettingsEntity
{
    /// <summary>
    /// Primary key — always 1 (singleton row).
    /// </summary>
    public int Id { get; set; } = 1;

    /// <summary>
    /// Encrypted DeepSeek API key.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Whether the proxy should start automatically on launch.
    /// </summary>
    public bool AutoRun { get; set; } = true;

    /// <summary>
    /// Base URL for the DeepSeek API.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.deepseek.com";

    /// <summary>
    /// Port the proxy listens on.
    /// </summary>
    public int Port { get; set; } = 5000;

    /// <summary>
    /// Id of the default AiModel (resolved manually in service).
    /// </summary>
    public int? DefaultModelId { get; set; }

    /// <summary>
    /// Maximum number of messages to forward (0 = all).
    /// </summary>
    public int MaxMessages { get; set; } = 0;

    /// <summary>
    /// Interval in seconds for balance refresh.
    /// </summary>
    public int BalanceRefreshIntervalSec { get; set; } = 60;
}
