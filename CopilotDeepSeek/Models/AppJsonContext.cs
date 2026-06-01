namespace CopilotDeepSeek.Models;

using System.Text.Json.Serialization;


[JsonSerializable(typeof(Settings))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal partial class AppJsonContext : JsonSerializerContext
{

}