using CopilotDeepSeek.Models;
using CopilotDeepSeek.Services;

namespace CopilotDeepSeek;

/// <summary>
/// Result from bootstrap containing resolved dependencies.
/// Note: Settings is a mutable reference type shared across the application.
/// </summary>
internal sealed record BootstrapResult(
    ISettingsService SettingsService,
    IServiceProvider ServiceProvider,
    Settings Settings,
    VerbosityLevel Verbosity,
    bool Hidden
);