using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Utils;

internal static class ReasoningEffortInjector
{
    /// <summary>
    /// DeepSeek thinking defaults to enabled, and reasoning_effort default value is 'high'
    /// This function injects reasoning_effort:'max' if reasoning_effort is not present
    /// </summary>
    /// <param name="body"></param>
    /// <returns></returns>
    internal static string Inject(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            var bufferWriter = new ArrayBufferWriter<byte>();
            using var w = new Utf8JsonWriter(bufferWriter);

            w.WriteStartObject();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                    prop.WriteTo(w);
            }

            // Only add reasoning_effort if not already present
            if (!doc.RootElement.TryGetProperty("reasoning_effort", out _))
            {
                w.WriteString("reasoning_effort", "max");
            }

            w.WriteEndObject();
            w.Flush();

            return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
        }
        catch
        {
            return body;
        }
    }
}