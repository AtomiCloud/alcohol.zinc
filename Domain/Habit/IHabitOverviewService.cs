using CSharp_Result;
using Domain.Charity;

namespace Domain.Habit;

public record HabitVersionMeta(Guid Id, ushort Version, bool IsActive);

public record HabitOverviewItem
(
  Guid HabitId,
  string Name,
  string? NotificationTime,   // "HH:mm" or null
  string Timezone,
  string[] DaysOfWeek,
  decimal StakeAmount,
  string StakeCurrency,
  bool Enabled,
  CharityPrincipal? Charity,  // null for a habit without a money stake
  HabitStreakStatus Status,
  int TimeLeftToEodMinutes,
  HabitVersionMeta Version,
  decimal TotalDebtAmount,
  // End of today's check (absolute): the user's next local midnight, null when the habit isn't scheduled today.
  DateTime? TodayDeadlineUtc,
  // How the habit is proven: "nfc" when a tag is linked, otherwise "manual".
  string Proof,
  Guid? RoutineId,
  int SortOrder,    // position in the user's habit list; items come in this order
  string? HabitTime // "HH:mm" or null: when the user plans to do it
);

public record HabitOverviewSearch(string UserId, int Limit, int Skip);

public record HabitOverviewSummary(
  List<HabitOverviewItem> Items,
  decimal TotalUserDebtAmount,
  int UsedSkip,
  int TotalSkip
);

public interface IHabitOverviewService
{
  Task<Result<HabitOverviewSummary>> GetOverview(HabitOverviewSearch search, string skipsMonthlyKey, DateTime? nowUtc = null);
}
