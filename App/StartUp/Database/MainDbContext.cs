using App.Modules.Causes.Data;
using App.Modules.Charities.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.Payment.Data;
using App.Modules.Users.Data;
using App.StartUp.Options;
using App.StartUp.Services;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.StartUp.Database;

public class MainDbContext(IOptionsMonitor<Dictionary<string, DatabaseOption>> options, ILoggerFactory factory)
  : DbContext
{
  public const string Key = "MAIN";


  public DbSet<UserData> Users { get; set; }
  public DbSet<ConfigurationData> Configurations { get; set; }
  public DbSet<CharityData> Charities { get; set; }
  public DbSet<CauseData> Causes { get; set; }
  public DbSet<CharityCauseData> CharityCauses { get; set; }
  public DbSet<ExternalIdData> ExternalIds { get; set; }
  public DbSet<HabitData> Habits { get; set; }
  public DbSet<HabitVersionData> HabitVersions { get; set; }
  public DbSet<HabitExecutionData> HabitExecutions { get; set; }
  public DbSet<PaymentCustomerData> PaymentCustomers { get; set; }
  public DbSet<PaymentIntentData> PaymentIntents { get; set; }
  // public DbSet<CompletionData> Completions { get; set; }
  // public DbSet<StatsData> Stats { get; set; }

  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
  {
    optionsBuilder
      .UseLoggerFactory(factory)
      .AddPostgres(options.CurrentValue, Key)
      .UseExceptionProcessor();
  }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    var user = modelBuilder.Entity<UserData>();
    user.HasIndex(x => x.Username).IsUnique();
    
    // Configuration
    var configuration = modelBuilder.Entity<ConfigurationData>();
    configuration.HasIndex(c => c.UserId).IsUnique(); // One configuration per user

    // Habit configuration (main entity)
    var habit = modelBuilder.Entity<HabitData>();
    habit.HasIndex(h => h.Version);
    habit.HasIndex(h => h.UserId);
    habit.HasMany(h => h.Versions)
         .WithOne(hv => hv.Habit)
         .HasForeignKey(hv => hv.HabitId);
    
    // HabitVersion configuration (versioned details)
    var habitVersion = modelBuilder.Entity<HabitVersionData>();
    habitVersion.HasIndex(x => new { x.HabitId, x.Version }).IsUnique();  // Unique version per habit
    habitVersion.HasOne(hv => hv.Charity)
                .WithMany()
                .HasForeignKey(hv => hv.CharityId);
    habitVersion.HasMany(hv => hv.Executions)
                .WithOne(he => he.HabitVersion)
                .HasForeignKey(he => he.HabitVersionId);
    
    // HabitExecution configuration
    var habitExecution = modelBuilder.Entity<HabitExecutionData>();
    habitExecution.HasIndex(x => new { x.HabitVersionId, x.Date }).IsUnique();  // One execution per version per day

    // Charity configuration
    var charity = modelBuilder.Entity<CharityData>();
    charity.HasIndex(c => c.Name);
    charity.HasIndex(c => new { c.PrimaryRegistrationCountry, c.PrimaryRegistrationNumber });
    charity.HasIndex(c => c.Countries).HasMethod("gin");

    // Cause configuration
    var cause = modelBuilder.Entity<CauseData>();
    cause.HasIndex(c => c.Key).IsUnique();

    // CharityCause configuration (many-to-many via explicit join)
    var charityCause = modelBuilder.Entity<CharityCauseData>();
    charityCause.HasIndex(cc => new { cc.CharityId, cc.CauseId }).IsUnique();
    charityCause.HasOne<CharityData>()
                .WithMany()
                .HasForeignKey(cc => cc.CharityId)
                .OnDelete(DeleteBehavior.Cascade);
    charityCause.HasOne<CauseData>()
                .WithMany()
                .HasForeignKey(cc => cc.CauseId)
                .OnDelete(DeleteBehavior.Cascade);

    // ExternalId configuration
    var externalId = modelBuilder.Entity<ExternalIdData>();
    externalId.HasIndex(e => new { e.Source, e.ExternalKey }).IsUnique();
    externalId.HasOne<CharityData>()
              .WithMany()
              .HasForeignKey(e => e.CharityId)
              .OnDelete(DeleteBehavior.Cascade);

    // PaymentCustomer configuration
    var paymentCustomer = modelBuilder.Entity<PaymentCustomerData>();
    paymentCustomer.HasIndex(x => x.UserId).IsUnique();  // One payment customer per user
    paymentCustomer.HasIndex(x => x.AirwallexCustomerId);

    // PaymentIntent configuration
    var paymentIntent = modelBuilder.Entity<PaymentIntentData>();
    paymentIntent.HasIndex(x => x.UserId);
    paymentIntent.HasIndex(x => x.AirwallexPaymentIntentId).IsUnique();
    paymentIntent.HasIndex(x => x.Status);
    paymentIntent.HasIndex(x => x.MerchantOrderId);

  }
}
