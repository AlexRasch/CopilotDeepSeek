namespace CopilotDeepSeek.Models;

/// <summary>
/// Controls how much request activity is printed to the console.
/// </summary>
public enum VerbosityLevel
{
    /// <summary>No output.</summary>
    None = 0,

    /// <summary>Only failed requests (non-2xx or exceptions).</summary>
    FailuresOnly = 1,

    /// <summary>All requests including successful ones.</summary>
    All = 2
}