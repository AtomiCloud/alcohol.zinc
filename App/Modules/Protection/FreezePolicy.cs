using Domain.Protection;

namespace App.Modules.Protection;

public class FreezePolicy : IFreezePolicy
{
  // Policy: baseCap plus thresholds at 30, 80, 150 streak days,
  // then +1 for every additional 150 days beyond 150.
  public int ComputeFreezeMax(int baseCap, int userMaxStreak)
  {
    if (baseCap < 0) baseCap = 0;
    var bonus = 0;
    if (userMaxStreak >= 30) bonus += 1;
    if (userMaxStreak >= 80) bonus += 1;
    if (userMaxStreak >= 150)
    {
      bonus += 1;
      bonus += (userMaxStreak - 150) / 150; // +1 per 150 thereafter
    }
    return baseCap + bonus;
  }
}
