namespace CopilotDeepSeek.Models;

using System.Text.Json.Serialization;


[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(ApiResponse))]
[JsonSerializable(typeof(ProxyStats))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext
{

}