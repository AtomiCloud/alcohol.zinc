namespace Domain.Protection;

public interface IFreezePolicy
{
  int ComputeFreezeMax(string tier, int userMaxStreak);
}

