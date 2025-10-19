using App.Modules.Causes.Data;
using App.Modules.Charities.Data;
using App.StartUp.Database;
using App.StartUp.Options;
using Microsoft.EntityFrameworkCore;
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
    var dbContext = scope.ServiceProvider.GetRequiredService<MainDbContext>();

    try
    {
      // Seed test causes with deterministic IDs
      var causes = new[]
      {
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000001"),
          Key = "health",
          Name = "Health"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000002"),
          Key = "education",
          Name = "Education"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000003"),
          Key = "environment",
          Name = "Environment"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000004"),
          Key = "animals",
          Name = "Animals & Wildlife"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000005"),
          Key = "poverty",
          Name = "Poverty & Hunger"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000006"),
          Key = "human-rights",
          Name = "Human Rights"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000007"),
          Key = "disaster-relief",
          Name = "Disaster Relief"
        },
        new CauseData
        {
          Id = Guid.Parse("00000000-0000-0000-0000-000000000008"),
          Key = "children",
          Name = "Children & Youth"
        }
      };

      var causesCreated = 0;
      foreach (var cause in causes)
      {
        var exists = await dbContext.Causes.AnyAsync(c => c.Key == cause.Key, cancellationToken);
        if (!exists)
        {
          await dbContext.Causes.AddAsync(cause, cancellationToken);
          causesCreated++;
        }
      }

      // Seed test charities with deterministic IDs
      var charities = new[]
      {
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000001"),
          Name = "Doctors Without Borders",
          Slug = "doctors-without-borders",
          Mission = "Medical humanitarian organization providing emergency medical care",
          Countries = ["US", "GB", "FR"],
          WebsiteUrl = "https://www.doctorswithoutborders.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000002"),
          Name = "Red Cross",
          Slug = "red-cross",
          Mission = "Humanitarian organization providing emergency assistance and disaster relief",
          Countries = ["US", "GB", "AU"],
          WebsiteUrl = "https://www.redcross.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000003"),
          Name = "World Wildlife Fund",
          Slug = "wwf",
          Mission = "Conservation organization working to protect wildlife and natural habitats",
          Countries = ["US", "GB", "CH"],
          WebsiteUrl = "https://www.worldwildlife.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000004"),
          Name = "UNICEF",
          Slug = "unicef",
          Mission = "United Nations agency providing humanitarian aid to children worldwide",
          Countries = ["US", "GB", "FR", "CN"],
          WebsiteUrl = "https://www.unicef.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000005"),
          Name = "Feeding America",
          Slug = "feeding-america",
          Mission = "Nationwide network of food banks fighting domestic hunger",
          Countries = ["US"],
          WebsiteUrl = "https://www.feedingamerica.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000006"),
          Name = "Save the Children",
          Slug = "save-the-children",
          Mission = "International organization helping children in need through education and healthcare",
          Countries = ["US", "GB", "AU", "CA"],
          WebsiteUrl = "https://www.savethechildren.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000007"),
          Name = "Amnesty International",
          Slug = "amnesty-international",
          Mission = "Global movement campaigning for human rights and justice",
          Countries = ["GB", "US", "FR", "DE"],
          WebsiteUrl = "https://www.amnesty.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000008"),
          Name = "The Nature Conservancy",
          Slug = "nature-conservancy",
          Mission = "Environmental organization working to protect ecologically important lands and waters",
          Countries = ["US"],
          WebsiteUrl = "https://www.nature.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000009"),
          Name = "Habitat for Humanity",
          Slug = "habitat-for-humanity",
          Mission = "Building homes and hope for families in need of decent and affordable housing",
          Countries = ["US", "CA", "GB"],
          WebsiteUrl = "https://www.habitat.org"
        },
        new CharityData
        {
          Id = Guid.Parse("10000000-0000-0000-0000-000000000010"),
          Name = "Khan Academy",
          Slug = "khan-academy",
          Mission = "Nonprofit providing free, world-class education for anyone, anywhere",
          Countries = ["US"],
          WebsiteUrl = "https://www.khanacademy.org"
        }
      };

      var charitiesCreated = 0;
      foreach (var charity in charities)
      {
        var exists = await dbContext.Charities.AnyAsync(c => c.Slug == charity.Slug, cancellationToken);
        if (!exists)
        {
          await dbContext.Charities.AddAsync(charity, cancellationToken);
          charitiesCreated++;
        }
      }

      // Link charities to causes
      var charityCauses = new[]
      {
        // Doctors Without Borders -> Health
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000001"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000001"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        },
        // Red Cross -> Health
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000002"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        },
        // Red Cross -> Disaster Relief
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000003"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000002"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000007")
        },
        // WWF -> Environment
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000004"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000003")
        },
        // WWF -> Animals
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000005"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000003"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000004")
        },
        // UNICEF -> Children
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000006"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000008")
        },
        // UNICEF -> Education
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000007"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000002")
        },
        // UNICEF -> Health
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000008"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        },
        // UNICEF -> Poverty
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000009"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000004"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000005")
        },
        // Feeding America -> Poverty
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000010"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000005"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000005")
        },
        // Save the Children -> Children
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000011"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000006"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000008")
        },
        // Save the Children -> Education
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000012"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000006"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000002")
        },
        // Save the Children -> Health
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000013"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000006"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000001")
        },
        // Save the Children -> Poverty
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000014"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000006"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000005")
        },
        // Amnesty International -> Human Rights
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000015"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000007"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000006")
        },
        // The Nature Conservancy -> Environment
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000016"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000008"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000003")
        },
        // Habitat for Humanity -> Poverty
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000017"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000009"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000005")
        },
        // Khan Academy -> Education
        new CharityCauseData
        {
          Id = Guid.Parse("20000000-0000-0000-0000-000000000018"),
          CharityId = Guid.Parse("10000000-0000-0000-0000-000000000010"),
          CauseId = Guid.Parse("00000000-0000-0000-0000-000000000002")
        }
      };

      var charityCausesCreated = 0;
      foreach (var charityCause in charityCauses)
      {
        var exists = await dbContext.CharityCauses.AnyAsync(
          cc => cc.CharityId == charityCause.CharityId && cc.CauseId == charityCause.CauseId,
          cancellationToken);
        if (!exists)
        {
          await dbContext.CharityCauses.AddAsync(charityCause, cancellationToken);
          charityCausesCreated++;
        }
      }

      // Save all changes in a single transaction
      if (causesCreated > 0 || charitiesCreated > 0 || charityCausesCreated > 0)
      {
        await dbContext.SaveChangesAsync(cancellationToken);
      }

      logger.LogInformation(
        "Data seeding completed: {CausesCreated} causes, {CharitiesCreated} charities, {LinkagesCreated} charity-cause linkages created",
        causesCreated, charitiesCreated, charityCausesCreated);
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
