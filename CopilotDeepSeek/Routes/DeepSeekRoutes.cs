using CopilotDeepSeek.Constants;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace CopilotDeepSeek.Routes
{
    public static class DeepSeekRoutes
    {
        public static async Task HandleDeepSeekModelsAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                await ctx.HandleSimpleGetAsync(context, ctx.GetEndpointUrl(ApiEndpoints.Models), useBearerToken: true);
            }
            catch { }
        }

        public static async Task HandleDeepSeekBalanceAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                await ctx.HandleSimpleGetAsync(context, ctx.GetEndpointUrl(ApiEndpoints.UserBalance), useBearerToken: true);
            }
            catch { }
        }

    }
}