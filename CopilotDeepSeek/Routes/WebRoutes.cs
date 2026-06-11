using CopilotDeepSeek.Models;
using CopilotDeepSeek.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Routes
{
    public static class WebRoutes
    {
        public static async Task HandleWebSettingsReadAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                using var scope = ctx.ScopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                var settings = settingsService.LoadForWebInterface();

                await RequestContext.RespondJsonAsync(context, 200,
                    ApiResponse.OkWithData(JsonSerializer.SerializeToElement(settings, AppJsonContext.Default.Settings)));
            }
            catch (Exception ex)
            {
                await RequestContext.RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
            }
        }

        public static async Task HandleWebSettingsSaveAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                using var reader = new StreamReader(context.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync();

                var settings = JsonSerializer.Deserialize<Settings>(body, AppJsonContext.Default.Settings);
                if (settings is null)
                {
                    await RequestContext.RespondJsonAsync(context, 400, ApiResponse.ErrorResponse("Invalid JSON body."));
                    return;
                }

                using var scope = ctx.ScopeFactory.CreateScope();
                var settingsService = scope.ServiceProvider.GetRequiredService<ISettingsService>();
                settingsService.Save(settings);

                await RequestContext.RespondJsonAsync(
                    context,
                    200,
                    ApiResponse.OkWithData(JsonSerializer.SerializeToElement(
                        new ApiResponse { },
                        AppJsonContext.Default.ApiResponse)
                    ));
            }
            catch (Exception ex)
            {
                await RequestContext.RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
            }
        }

    }
}
