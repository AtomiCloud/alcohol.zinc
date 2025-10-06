using CSharp_Result;
using Domain.Charity;

namespace Domain.Habit;

public record HabitVersionMeta(Guid Id, ushort Version, bool IsActive);

public record HabitOverviewItem
(
  Guid HabitId,
  string Name,
  string NotificationTime,
  string Timezone,
  string[] DaysOfWeek,
  decimal StakeAmount,
  string StakeCurrency,
  bool Enabled,
  CharityPrincipal Charity,
  HabitStreakStatus Status,
  int TimeLeftToEodMinutes,
  HabitVersionMeta Version
);

public record HabitOverviewSearch(string UserId, int Limit, int Skip);

public interface IHabitOverviewService
{
  Task<Result<List<HabitOverviewItem>>> GetOverview(HabitOverviewSearch search, DateTime? nowUtc = null);
}
