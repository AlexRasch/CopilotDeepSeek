using CopilotDeepSeek.Models;
using CopilotDeepSeek.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Text;
using System.Text.Json;

namespace CopilotDeepSeek.Routes
{
    public static class WebRoutes
    {
        public static async Task HandleIndexRequest(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "www", "index.html");

                if (!File.Exists(path))
                {
                    context.Response.StatusCode = 404;
                    return;
                }

                var bytes = await File.ReadAllBytesAsync(path);
                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength64 = bytes.Length;
                await context.Response.OutputStream.WriteAsync(bytes);
            }
            catch (Exception ex)
            {
                context.Response.StatusCode = 500;
                var error = Encoding.UTF8.GetBytes($"{{\"error\":\"{ex.Message}\"}}");
                await context.Response.OutputStream.WriteAsync(error);
            }
            finally
            {
                context.Response.Close();
            }
        }

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

        public static async Task HandleRequestStatsAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                using var scope = ctx.ScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

                var stats = await dbContext.ProxyRequests
                    .GroupBy(r => 1)
                    .Select(g => new ProxyStats
                    {
                        TotalRequests = g.Count(),
                        SuccessfulRequests = g.Count(r => r.IsSuccess),
                        FailedRequests = g.Count(r => !r.IsSuccess),
                        AverageElapsedMs = g.Average(r => r.ElapsedMs)
                    })
                    .FirstOrDefaultAsync();

                if (stats == null)
                {
                    JsonSerializer.SerializeToElement(
                        new ProxyStats
                        {
                            TotalRequests = 0,
                            SuccessfulRequests = 0,
                            FailedRequests = 0,
                            AverageElapsedMs = 0
                        },
                    AppJsonContext.Default.ProxyStats);
                }

                await RequestContext.RespondJsonAsync(context, 200, ApiResponse.OkWithData(JsonSerializer.SerializeToElement(stats, AppJsonContext.Default.ProxyStats)));
            }
            catch (Exception ex)
            {
                await RequestContext.RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
            }
        }

        public static async Task HandleRequestLogAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                var query = context.Request.Url?.Query;
                if (string.IsNullOrEmpty(query))
                {
                    await RequestContext.RespondJsonAsync(context, 400, ApiResponse.ErrorResponse("Missing 'id' query parameter."));
                    return;
                }

                var queryParams = System.Web.HttpUtility.ParseQueryString(query);
                var idStr = queryParams["id"];

                if (string.IsNullOrEmpty(idStr) || !Guid.TryParse(idStr, out var id))
                {
                    await RequestContext.RespondJsonAsync(context, 400, ApiResponse.ErrorResponse("Invalid or missing 'id' query parameter."));
                    return;
                }

                using var scope = ctx.ScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

                var logEntry = await dbContext.ProxyRequests.FindAsync(id);

                if (logEntry is null)
                {
                    await RequestContext.RespondJsonAsync(context, 404, ApiResponse.ErrorResponse("Log entry not found."));
                    return;
                }

                await RequestContext.RespondJsonAsync(context, 200, ApiResponse.OkWithData(
                    JsonSerializer.SerializeToElement(logEntry, AppJsonContext.Default.ProxyRequest)));
            }
            catch (Exception ex)
            {
                await RequestContext.RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
            }
        }

        public static async Task HandleRequestsLogsAsync(HttpListenerContext context, RequestContext ctx)
        {
            try
            {
                using var scope = ctx.ScopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<CopilotDeepSeek.Database.AppDbContext>();

                var query = context.Request.Url?.Query;
                DateTime? fromDate = null;
                DateTime? toDate = null;
                var page = 0;
                var amount = 50;

                if (!string.IsNullOrEmpty(query))
                {
                    var queryParams = System.Web.HttpUtility.ParseQueryString(query);

                    if (DateTime.TryParse(queryParams["from"], out var parsedFrom))
                        fromDate = parsedFrom;

                    if (DateTime.TryParse(queryParams["to"], out var parsedTo))
                        toDate = parsedTo;

                    if (int.TryParse(queryParams["page"], out var parsedPage) && parsedPage >= 0)
                        page = parsedPage;

                    if (int.TryParse(queryParams["amount"], out var parsedAmount) && parsedAmount > 0)
                        amount = Math.Min(parsedAmount, 1000);
                }

                var logsQuery = dbContext.ProxyRequests.AsQueryable();

                if (fromDate.HasValue)
                    logsQuery = logsQuery.Where(r => r.Timestamp >= fromDate.Value);

                if (toDate.HasValue)
                    logsQuery = logsQuery.Where(r => r.Timestamp < toDate.Value.AddDays(1));

                var totalCount = await logsQuery.CountAsync();
                var totalPages = totalCount > 0 ? (int)Math.Ceiling((double)totalCount / amount) : 0;

                // Clamp page to valid range
                if (totalPages > 0 && page >= totalPages)
                    page = totalPages - 1;
                else if (page < 0)
                    page = 0;


                var logs = await logsQuery
                    .OrderByDescending(r => r.Timestamp)
                    .Skip(page * amount)
                    .Take(amount)
                    .ToListAsync();

                var response = new ProxyRequestLogsResponse
                {
                    TotalCount = totalCount,
                    Logs = logs,
                    CurrentPage = page,
                    TotalPages = totalPages,
                    PageSize = amount
                };

                await RequestContext.RespondJsonAsync(context, 200, ApiResponse.OkWithData(JsonSerializer.SerializeToElement(response, AppJsonContext.Default.ProxyRequestLogsResponse)));
            }
            catch (Exception ex)
            {
                await RequestContext.RespondJsonAsync(context, 500, ApiResponse.ErrorResponse(ex.Message));
            }
        }
    }
}
