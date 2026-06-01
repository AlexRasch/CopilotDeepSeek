using System;
using System.Collections.Generic;
using System.Text;

namespace CopilotDeepSeek.Models
{
    public class Settings
    {
        public string ApiKey { get; set; } = string.Empty;
        public bool AutoRun { get; set; } = true;
        public string BaseUrl { get; set; } = "https://api.deepseek.com";
        public string Model { get; set; } = "deepseek-v4-flash";
        public int Port { get; set; } = 5000;
    }
}
