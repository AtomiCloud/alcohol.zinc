using CSharp_Result;

namespace Domain.Protection;

public interface IProtectionAwardService
{
  // Evaluates prior week for the user and awards freeze days per-habit for perfect weeks.
  // Returns number of habit awards recorded (idempotent via ledger).
  Task<Result<int>> AwardWeeklyFreezesForUser(string userId, DateTime? nowUtc = null);
}

