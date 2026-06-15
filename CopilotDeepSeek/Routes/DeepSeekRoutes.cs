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
                await ctx.HandleSimpleGetAsync(context, ctx.TargetBase + "/models");
            }
            catch { }
        }

        public static async Task HandleDeepSeekBalanceAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                await ctx.HandleSimpleGetAsync(context, ctx.TargetBase + "/balance");
            }
            catch { }
        }

    }
}