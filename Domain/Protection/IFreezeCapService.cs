using CSharp_Result;

namespace Domain.Protection;

public interface IFreezeCapService
{
  // Cuts the user's banked freezes to their current cap (plan and streak bonus) and returns
  // the balance and cap. Cut freezes are gone; a later higher cap does not restore them.
  Task<Result<FreezeBalance>> Reconcile(string userId);
}
