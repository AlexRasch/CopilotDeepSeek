namespace CopilotDeepSeek.Models;

public class Settings
{
    public string ApiKey { get; set; } = string.Empty;
    public bool HasApiKey { get; set; }
    public bool AutoRun { get; set; } = true;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";
    public int Port { get; set; } = 5000;
    public List<string> AllowedModels { get; set; } = [];
    public int MaxMessages { get; set; } = 0;
    public int BalanceRefreshIntervalSec { get; set; } = 60;
}
