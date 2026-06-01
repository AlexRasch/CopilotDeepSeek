using CopilotDeepSeek.Models;

namespace CopilotDeepSeek.Services;

internal interface ISettingsService
{
    Settings LoadOrCreate();
    void Save(Settings settings);
    void Reset();
}