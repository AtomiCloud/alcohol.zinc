namespace Domain.Allowance;

public record UserMonthWindow(
  string Timezone,
  DateOnly MonthStart,
  DateOnly MonthEnd,
  DateTime UserNowLocal
);

