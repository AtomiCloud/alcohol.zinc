namespace Domain.Protection;

public interface IFreezePolicy
{
  // Computes the maximum freeze cap using a base entitlement cap
  // and the user's max streak-derived bonus. Pure function.
  int ComputeFreezeMax(int baseCap, int userMaxStreak);
}
