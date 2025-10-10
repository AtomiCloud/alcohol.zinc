using CSharp_Result;

namespace Domain.Protection;

public interface IProtectionRepository
{
  Task<Result<UserProtectionPrincipal?>> GetProtection(string userId);
  Task<Result<UserProtectionPrincipal>> UpsertProtection(string userId);

  // Single user-level freeze day consumption (one per date)
  Task<Result<bool>> TryConsumeFreeze(string userId, DateOnly date);

  // Award n freeze days to user-level pool (cap handled by service/policy; repo should only persist)
  Task<Result<Unit>> IncrementFreeze(string userId, int n);

  // Ledgers for idempotency
  Task<Result<bool>> RecordFreezeAwardIfAbsent(Guid habitId, DateOnly weekStart);

  // Clamp balance to cap (for downgrades)
  Task<Result<Unit>> ClampFreezeToCap(string userId, int cap);
}
