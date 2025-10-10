using CSharp_Result;

namespace Domain.Subscription;

public interface ISubscriptionService
{
  Task<Result<string>> GetUserTier(string userId);
  Task<Result<int>> GetLimitForTier(string tier, string key);
}
