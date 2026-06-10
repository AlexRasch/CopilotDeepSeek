namespace CopilotDeepSeek.Models;

using CopilotDeepSeek.Database.Entities;
using System.Text.Json.Serialization;


[JsonSerializable(typeof(ApiResponse))]

[JsonSerializable(typeof(ProxyStats))]
[JsonSerializable(typeof(IEnumerable<ProxyRequest>))]
[JsonSerializable(typeof(ProxyRequestLogsResponse))]
[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
internal partial class AppJsonContext : JsonSerializerContext
{

}