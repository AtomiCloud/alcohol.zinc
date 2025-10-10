using App.StartUp.Options;
using Domain.Cause;
using Domain.Charity;
using Microsoft.Extensions.Options;

namespace App.StartUp.Seeding;

public class DataSeederHostedService(
  IServiceProvider serviceProvider,
  IOptionsMonitor<AppOption> app,
  ILogger<DataSeederHostedService> logger)
  : IHostedService
{
  public async Task StartAsync(CancellationToken cancellationToken)
  {
    // Only seed data in lapras landscape
    if (!string.Equals(app.CurrentValue.Landscape, "lapras", StringComparison.OrdinalIgnoreCase))
    {
      logger.LogInformation("Skipping data seeding - not in lapras landscape (current: {Landscape})",
        app.CurrentValue.Landscape);
      return;
    }

    logger.LogInformation("Starting data seeding for lapras landscape");

    using var scope = serviceProvider.CreateScope();
    var causeService = scope.ServiceProvider.GetRequiredService<ICauseService>();
    var charityService = scope.ServiceProvider.GetRequiredService<ICharityService>();

    try
    {
      // Seed a few test causes
      var causes = new[]
      {
        new CauseRecord { Key = "health", Name = "Health" },
        new CauseRecord { Key = "education", Name = "Education" },
        new CauseRecord { Key = "environment", Name = "Environment" }
      };

      var causesCreated = 0;
      foreach (var cause in causes)
      {
        var existing = await causeService.GetByKey(cause.Key);
        if (existing.IsSuccess() && existing.SuccessOrDefault() == null)
        {
          await causeService.Create(cause);
          causesCreated++;
        }
      }

      // Seed a few test charities
      var charities = new[]
      {
        new CharityRecord
        {
          Name = "Doctors Without Borders",
          Slug = "doctors-without-borders",
          Mission = "Medical humanitarian organization",
          Countries = ["US", "GB", "FR"],
          WebsiteUrl = "https://www.doctorswithoutborders.org"
        },
        new CharityRecord
        {
          Name = "Red Cross",
          Slug = "red-cross",
          Mission = "Humanitarian organization providing emergency assistance",
          Countries = ["US", "GB", "AU"],
          WebsiteUrl = "https://www.redcross.org"
        }
      };

      var charitiesCreated = 0;
      foreach (var charity in charities)
      {
        var existing = await charityService.Search(new CharitySearch
        {
          Slug = charity.Slug,
          Limit = 1,
          Skip = 0
        });

        if (existing.IsSuccess() && !existing.SuccessOrDefault()?.Any() == true)
        {
          await charityService.Create(charity);
          charitiesCreated++;
        }
      }

      logger.LogInformation(
        "Data seeding completed: {CausesCreated} causes, {CharitiesCreated} charities created",
        causesCreated, charitiesCreated);
    }
    catch (Exception ex)
    {
      logger.LogWarning(ex, "Data seeding encountered an exception but will not stop application startup");
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    return Task.CompletedTask;
  }
}
