using App.StartUp.Registry;
using CSharp_Result;
using Domain.Subscription;

namespace App.StartUp.Services.Subscription;

// Temporary subscription implementation until Lagos-backed service is integrated.
// Returns sensible defaults per tier key for development.
public class NullSubscriptionService : ISubscriptionService
{
  public Task<Result<string>> GetUserTier(string userId)
  {
    return Task.FromResult((Result<string>)"free");
  }

  public Task<Result<int>> GetLimitForTier(string tier, string key)
  {
    // Defaults can be tuned or sourced from config if needed.
    var value = key switch
    {
      EntitlementKeys.HabitsMax => 10,
      EntitlementKeys.SkipsMonthly => 10,
      EntitlementKeys.VacationWindowsYearly => 3,
      EntitlementKeys.FreezeBase => 7,
      _ => int.MaxValue // permissive default
    };
    return Task.FromResult((Result<int>)value);
  }
}
