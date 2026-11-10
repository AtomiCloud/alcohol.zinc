using CSharp_Result;

namespace Domain.Proof;

public interface IProofBindingRepository
{
  // The binding of a non-deleted habit, or null when it only has the always-bound proofs.
  Task<Result<ProofBinding?>> GetForHabit(Guid habitId);

  // The user's non-deleted habits bound to the plugin.
  Task<Result<List<ProofBinding>>> ListForUser(string userId, string pluginKey);

  // Binds the habit, replacing its existing binding (one per habit).
  Task<Result<ProofBindingPrincipal>> Upsert(Guid habitId, ProofBindingRecord record);

  Task<Result<Unit?>> Delete(Guid habitId);
}
