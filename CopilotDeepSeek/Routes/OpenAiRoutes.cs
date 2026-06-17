using CopilotDeepSeek.Constants;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Routes;

/// <summary>
/// Handlers for OpenAI-compatible endpoints (e.g. /v1/chat/completions).
/// </summary>
public static class OpenAiRoutes
{
    /// <summary>
    /// Handles POST /v1/chat/completions by injecting cached reasoning_content,
    /// forwarding to DeepSeek, and caching reasoning from the response.
    /// </summary>
    public static async Task HandleChatCompletionsAsync(HttpListenerContext context, RequestContext ctx)
    {
        try
        {
            // Read request body
            using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            RequestContext.DumpJson("OPENAI INCOMING", body);

            // Inject cached reasoning_content into assistant messages
            var requestBody = ctx.ModifyRequestBody(body, out var isStreaming);

            // Forward to DeepSeek /chat/completions
            using var forwardResponse = await ctx.SendForwardRequestAsync(
                ctx.GetEndpointUrl(ApiEndpoints.ChatCompletions),
                requestBody,
                isStreaming);

            // Copy response status and headers
            context.Response.StatusCode = (int)forwardResponse.StatusCode;
            context.Response.StatusDescription = forwardResponse.ReasonPhrase;

            foreach (var header in forwardResponse.Headers)
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);

            foreach (var header in forwardResponse.Content.Headers)
                context.Response.Headers[header.Key] = string.Join(", ", header.Value);

            // Handle streaming vs non-streaming
            if (isStreaming && forwardResponse.IsSuccessStatusCode)
            {
                await ctx.StreamAndCacheResponseAsync(forwardResponse, context.Response);
            }
            else
            {
                var responseBody = await forwardResponse.Content.ReadAsStringAsync();
                RequestContext.DumpJson("OPENAI RESPONSE", responseBody);

                if (forwardResponse.IsSuccessStatusCode)
                {
                    ctx.CacheReasoningFromResponse(responseBody);
                }

                var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                await context.Response.OutputStream.WriteAsync(responseBytes);
            }
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = 502;
            var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
            await context.Response.OutputStream.WriteAsync(error);
        }
    }
}