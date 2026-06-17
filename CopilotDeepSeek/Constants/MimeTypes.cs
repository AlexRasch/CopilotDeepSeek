using System;
using System.Collections.Generic;
using System.Text;

namespace CopilotDeepSeek.Constants
{
    internal static class MimeTypes
    {

        internal static readonly Dictionary<string, string> StaticMimeTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            { ".html", "text/html; charset=utf-8" },
            { ".css",  "text/css" },
            { ".js",   "application/javascript" },
            { ".json", ContentTypes.ApplicationJson },
            { ".png",  "image/png" },
            { ".jpg",  "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".gif",  "image/gif" },
            { ".svg",  "image/svg+xml" },
            { ".ico",  "image/x-icon" },
            { ".woff", "font/woff" },
            { ".woff2","font/woff2" },
            { ".ttf",  "font/ttf" },
            { ".map",  ContentTypes.ApplicationJson },
        };

    }
}
