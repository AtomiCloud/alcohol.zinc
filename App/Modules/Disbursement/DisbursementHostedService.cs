using App.StartUp.Options;
using Domain.Disbursement;
using Microsoft.Extensions.Options;

namespace App.Modules.Disbursement;

// Scheduled charity payout. Once a day it reconciles any in-flight disbursements, then claims the
// charged-but-unpaid penalties grouped by (charity, currency) and donates each via Pledge. The
// atomic claim (DisbursementId stamped under a row lock) prevents double-donation across passes;
// this in-process gate just avoids overlapping work.
public class DisbursementHostedService(
  IServiceProvider serviceProvider,
  IOptionsMonitor<DisbursementOption> options,
  ILogger<DisbursementHostedService> logger
) : IHostedService, IDisposable
{
  private Timer? _timer;
  private readonly SemaphoreSlim _gate = new(1, 1);

  public Task StartAsync(CancellationToken cancellationToken)
  {
    if (!options.CurrentValue.Enabled)
    {
      logger.LogInformation("DisbursementHostedService disabled (Disbursement.Enabled=false); not scheduling");
      return Task.CompletedTask;
    }

    // Daily payout. The longer startup delay lets the penalty drain settle the day's charges
    // (which is what creates pending payouts) before we sweep them.
    _timer = new Timer(async _ => await DoWork(), null, TimeSpan.FromMinutes(5), TimeSpan.FromHours(24));
    logger.LogInformation("DisbursementHostedService started");
    return Task.CompletedTask;
  }

  private async Task DoWork()
  {
    if (!await _gate.WaitAsync(0))
    {
      logger.LogInformation("Disbursement previous pass still running; skipping tick");
      return;
    }
    try
    {
      var opt = options.CurrentValue;
      var donor = new DonorIdentity
      {
        FirstName = opt.DonorFirstName,
        LastName = opt.DonorLastName,
        Email = opt.DonorEmail
      };

      using var scope = serviceProvider.CreateScope();
      var svc = scope.ServiceProvider.GetRequiredService<IDisbursementService>();
      var res = await svc.ProcessPending(donor, opt.MinPayoutCents, opt.MaxGroupsPerRun);
      if (res.IsSuccess())
        logger.LogInformation("Disbursement paid out {Count} group(s)", (int)res);
      else
        logger.LogError(res.FailureOrDefault(), "Disbursement error");
    }
    catch (Exception e)
    {
      logger.LogError(e, "DisbursementHostedService failed");
    }
    finally
    {
      _gate.Release();
    }
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _timer?.Change(Timeout.Infinite, 0);
    logger.LogInformation("DisbursementHostedService stopping");
    return Task.CompletedTask;
  }

  public void Dispose()
  {
    _timer?.Dispose();
    _gate.Dispose();
  }
}
