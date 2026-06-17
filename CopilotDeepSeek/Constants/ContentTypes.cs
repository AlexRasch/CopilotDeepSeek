using System.Net.Mime;

namespace CopilotDeepSeek.Constants
{
    internal static class ContentTypes
    {
        private const string CharsetUtf8 = "; charset=utf-8";
        internal const string ApplicationJson = MediaTypeNames.Application.Json;
        internal const string ApplicationJsonWithCharset = ApplicationJson + CharsetUtf8;
    }
}