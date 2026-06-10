using System;
using System.Collections.Generic;
using System.Text;

namespace CopilotDeepSeek.Constants
{
    /// <summary>
    /// Enum of HTTP request methods
    /// </summary>
    public enum ProxyRequestMethod : byte
    {
        GET = 0,
        HEAD = 1,
        OPTIONS = 2,
        TRACE = 3,
        PUT = 4,
        DELETE = 5,
        POST = 6,
        PATCH = 7,
        CONNECT = 8,
    }
}