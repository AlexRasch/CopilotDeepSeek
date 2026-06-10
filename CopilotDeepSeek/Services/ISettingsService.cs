using CopilotDeepSeek.Models;

namespace CopilotDeepSeek.Services;

internal interface ISettingsService
{
    Settings LoadOrCreate();
    Settings LoadForWebInterface();
    void Save(Settings settings);
    void Reset();
}