using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace CopilotDeepSeek.Routes
{
    public static class ProxyRoutes
    {
        public static async Task HandleStart(HttpListenerContext context, RequestContext ctx)
        {
            ctx.AllowDeepSeek = true;
            await RequestContext.RespondJsonAsync(context, 200, Models.ApiResponse.DeepSeekEnabled());
        }

        public static async Task HandleStop(HttpListenerContext context, RequestContext ctx)
        {
            ctx.AllowDeepSeek = false;
            await RequestContext.RespondJsonAsync(context, 200, Models.ApiResponse.DeepSeekDisabled());
        }

    }
}
