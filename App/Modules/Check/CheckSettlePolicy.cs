using App.StartUp.Options;
using Domain.Check;
using Domain.Strava;
using Microsoft.Extensions.Options;

namespace App.Modules.Check;

// Strava-bound habits settle Strava.UploadWaitMinutes after local midnight; the rest after
// CheckWindow.Grace.
public class CheckSettlePolicy(IOptionsMonitor<StravaOption> strava) : ICheckSettlePolicy
{
  public IReadOnlyDictionary<string, TimeSpan> SlowProofs => new Dictionary<string, TimeSpan>
  {
    [StravaProofPlugin.Key] = TimeSpan.FromMinutes(strava.CurrentValue.UploadWaitMinutes)
  };
}
