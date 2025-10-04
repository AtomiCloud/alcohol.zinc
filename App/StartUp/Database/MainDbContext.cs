using App.Modules.Charities.Data;
using App.Modules.Configurations.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
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
  public DbSet<HabitData> Habits { get; set; }
  public DbSet<HabitVersionData> HabitVersions { get; set; }
  public DbSet<HabitExecutionData> HabitExecutions { get; set; }
  // public DbSet<CompletionData> Completions { get; set; }
  // public DbSet<StatsData> Stats { get; set; }

  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
  {
    optionsBuilder
      .UseLoggerFactory(factory)
      .EnableSensitiveDataLogging()
      .EnableDetailedErrors()
      .AddPostgres(options.CurrentValue, Key)
      .UseExceptionProcessor();
  }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    var user = modelBuilder.Entity<UserData>();
    user.HasIndex(x => x.Username).IsUnique();
    
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
    
  }
}
