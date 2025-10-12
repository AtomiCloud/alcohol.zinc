using Domain.Habit;

namespace App.Modules.Habit;

public class HabitDailyFailureHostedService(
  IServiceProvider serviceProvider,
  ILogger<HabitDailyFailureHostedService> logger
) : IHostedService, IDisposable
{
  private Timer? _timer;

  public Task StartAsync(CancellationToken cancellationToken)
  {
    // Run every 15 minutes, small initial delay
    _timer = new Timer(async _ => await DoWork(), null, TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));
    logger.LogInformation("HabitDailyFailureHostedService started");
    return Task.CompletedTask;
  }

  private async Task DoWork()
  {
    try
    {
      using var scope = serviceProvider.CreateScope();
      var habitService = scope.ServiceProvider.GetRequiredService<IHabitService>();
      var res = await habitService.MarkDailyFailuresForTimezonesNearMidnight();
      if (res.IsSuccess())
      {
        logger.LogInformation("HabitDailyFailureHostedService marked failures: {Count}", (int)res);
      }
      else
      {
        logger.LogError(res.FailureOrDefault(), "HabitDailyFailureHostedService encountered an error");
      }
    }
    catch (Exception e)
    {
      logger.LogError(e, "HabitDailyFailureHostedService failed");
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _timer?.Change(Timeout.Infinite, 0);
    logger.LogInformation("HabitDailyFailureHostedService stopping");
    return Task.CompletedTask;
  }

  public void Dispose()
  {
    _timer?.Dispose();
  }
}

