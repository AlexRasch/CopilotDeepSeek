using CopilotDeepSeek.Database;
using CopilotDeepSeek.Database.Entities;
using CopilotDeepSeek.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace CopilotDeepSeek.Services;

internal sealed class SettingsService(IServiceScopeFactory scopeFactory) : ISettingsService
{
    public Settings LoadOrCreate()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var entity = db.Settings.FirstOrDefault();

        var allowedModels = db.AiModels
            .Where(m => m.IsAllowed)
            .Select(m => m.Name)
            .ToList();

        if (entity is null)
        {
            // Seed defaults - check if model already exists to avoid UNIQUE constraint violation
            var defaultModel = db.AiModels.FirstOrDefault(m => m.Name == "deepseek-v4-flash");

            if (defaultModel is null)
            {
                defaultModel = new AiModel
                {
                    Name = "deepseek-v4-flash",
                    IsAllowed = true
                };
                db.AiModels.Add(defaultModel);
            }
            else
            {
                // Model exists but was disabled - re-enable it
                defaultModel.IsAllowed = true;
            }

            entity = new SettingsEntity
            {
                Id = 1,
                DefaultModelId = defaultModel.Id
            };
            db.Settings.Add(entity);
            db.SaveChanges();

            return MapToSettings(entity, defaultModel.Name, allowedModels.Count > 0 ? allowedModels : ["deepseek-v4-flash"]);
        }

        var defaultModelName = entity.DefaultModelId.HasValue
            ? db.AiModels.Where(m => m.Id == entity.DefaultModelId.Value).Select(m => m.Name).FirstOrDefault()
            : null;

        return MapToSettings(entity, defaultModelName ?? "deepseek-v4-flash", allowedModels);
    }

    public Settings LoadForWebInterface()
    {
        var settings = LoadOrCreate();

        // Set HasApiKey before clearing sensitive data
        settings.HasApiKey = !string.IsNullOrEmpty(settings.ApiKey);

        // Clear sensitive data for web interface
        settings.ApiKey = string.Empty;

        return settings;
    }

    public void Save(Settings settings)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Use a transaction to ensure atomicity - prevents orphaned AiModels if Settings save fails
        using var transaction = db.Database.BeginTransaction();

        try
        {
            // Sync AiModels
            var existingModels = db.AiModels.ToList();
            var incomingNames = settings.AllowedModels ?? [];

            foreach (var modelName in incomingNames)
            {
                var existing = existingModels.FirstOrDefault(m =>
                    m.Name.Equals(modelName, StringComparison.OrdinalIgnoreCase));

                if (existing is not null)
                {
                    existing.IsAllowed = true;
                }
                else
                {
                    db.AiModels.Add(new AiModel
                    {
                        Name = modelName,
                        IsAllowed = true
                    });
                }
            }

            // Disable models no longer in the allowed list
            foreach (var existing in existingModels)
            {
                if (!incomingNames.Contains(existing.Name, StringComparer.OrdinalIgnoreCase))
                {
                    existing.IsAllowed = false;
                }
            }
            db.SaveChanges();

            // Resolve the default model
            int? defaultModelId = null;
            if (!string.IsNullOrEmpty(settings.Model))
            {
                var defaultModel = existingModels
                    .FirstOrDefault(m => m.Name.Equals(settings.Model, StringComparison.OrdinalIgnoreCase));


                if (defaultModel is not null)
                {
                    defaultModelId = defaultModel.Id;
                    defaultModel.IsAllowed = true;
                }
            }

            // Singleton entity: update the existing row (Id is always 1)
            var existingSettings = db.Settings.FirstOrDefault();
            if (existingSettings is not null)
            {
                existingSettings.ApiKey = settings.ApiKey != "" ? SecurityHelper.Encrypt(settings.ApiKey) : "";
                existingSettings.AutoRun = settings.AutoRun;
                existingSettings.BaseUrl = settings.BaseUrl;
                existingSettings.Port = settings.Port;
                existingSettings.MaxMessages = settings.MaxMessages;
                existingSettings.BalanceRefreshIntervalSec = settings.BalanceRefreshIntervalSec;
                existingSettings.DefaultModelId = defaultModelId;
            }
            else
            {
                db.Settings.Add(new SettingsEntity
                {
                    Id = 1,
                    ApiKey = settings.ApiKey,
                    AutoRun = settings.AutoRun,
                    BaseUrl = settings.BaseUrl,
                    Port = settings.Port,
                    MaxMessages = settings.MaxMessages,
                    BalanceRefreshIntervalSec = settings.BalanceRefreshIntervalSec,
                    DefaultModelId = defaultModelId
                });
            }

            // Save all changes atomically
            db.SaveChanges();
            transaction.Commit();
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public void Reset()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.AiModels.RemoveRange(db.AiModels);
        db.Settings.RemoveRange(db.Settings);
        db.SaveChanges();

        Console.WriteLine("Config reset. Settings will be re-created on next launch.");
    }

    private static Settings MapToSettings(SettingsEntity entity, string defaultModelName, List<string> allowedModels)
    {
        return new Settings
        {
            ApiKey = entity.ApiKey,
            AutoRun = entity.AutoRun,
            BaseUrl = entity.BaseUrl,
            Model = defaultModelName,
            Port = entity.Port,
            MaxMessages = entity.MaxMessages,
            BalanceRefreshIntervalSec = entity.BalanceRefreshIntervalSec,
            AllowedModels = allowedModels
        };
    }

    // Mini Helpers

    public string getPort()
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var entity = db.Settings.FirstOrDefault();
        return entity?.Port.ToString() ?? "5000";
    }

}