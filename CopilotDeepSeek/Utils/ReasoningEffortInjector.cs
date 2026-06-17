using System.Buffers;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Utils;

internal static class ReasoningEffortInjector
{
    internal static string Inject(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("model", out var modelEl) ||
                modelEl.ValueKind != JsonValueKind.String)
                return body;

            var modelStr = modelEl.GetString();
            if (string.IsNullOrEmpty(modelStr) ||
                !modelStr.EndsWith(":max", StringComparison.Ordinal))
                return body;

            var bufferWriter = new ArrayBufferWriter<byte>();
            using var w = new Utf8JsonWriter(bufferWriter);

            w.WriteStartObject();

            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                if (prop.NameEquals("model"))
                {
                    w.WriteString("model",modelStr[..^4]);
                }
                else
                {
                    prop.WriteTo(w);
                }
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