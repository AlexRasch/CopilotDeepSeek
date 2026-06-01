using System.Text.Json;
using CopilotDeepSeek.Models;

namespace CopilotDeepSeek.Services;

internal sealed class SettingsService(string configPath = "settings.json") : ISettingsService
{

    public Settings LoadOrCreate()
    {
        if (!File.Exists(configPath))
            File.WriteAllText(configPath, JsonSerializer.Serialize(
                new Settings(), AppJsonContext.Default.Settings));

        var json = File.ReadAllText(configPath);
        return JsonSerializer.Deserialize(json, AppJsonContext.Default.Settings)!;
    }

    public void Save(Settings settings)
    {
        var json = JsonSerializer.Serialize(settings, AppJsonContext.Default.Settings);
        File.WriteAllText(configPath, json);
    }

    public void Reset()
    {
        if (!File.Exists(configPath))
            return;

        File.Delete(configPath);
        Console.WriteLine("Config reset. A new settings.json will be created.");
    }
}