namespace CopilotDeepSeek.Models;

using CopilotDeepSeek.Database.Entities;
using System.Text.Json.Serialization;


[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(ApiResponse))]

[JsonSerializable(typeof(ProxyStats))]
[JsonSerializable(typeof(IEnumerable<ProxyRequest>))]
[JsonSerializable(typeof(ProxyRequestLogsResponse))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext
{

}