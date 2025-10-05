public record CreateHabitReq(
    string Task,
    string[] DaysOfWeek,
    string NotificationTime, // string (e.g. "HH:mm")
    string Stake,            // string decimal (e.g. "10.50")
    Guid CharityId,
    string Timezone          // string (e.g. "Asia/Singapore", "America/New_York")
);

public record UpdateHabitReq(
    string Task,
    string[] DaysOfWeek,
    string NotificationTime,
    string Stake,
    Guid CharityId,
    bool Enabled,
    string Timezone          // string (e.g. "Asia/Singapore", "America/New_York")
);

public record HabitRes(
    Guid Id,                 // Habit ID (main entity)
    ushort Version,          // Current version number
    string UserId,
    bool Enabled             // Whether habit is enabled
);

public record HabitVersionRes(
    Guid Id,                 // HabitVersion ID
    Guid HabitId,            // References main habit
    ushort Version,
    string Task,
    string[] DaysOfWeek,
    string NotificationTime,
    string Stake,
    string Ratio,
    Guid CharityId,
    string Timezone          // string (e.g. "Asia/Singapore", "America/New_York")
);

public record MarkDailyFailuresReq(
    string Date,             // string (e.g. "31-08-2025")
    List<Guid> HabitIds     // List of user IDs to process
);

public record CompleteHabitReq(
    string? Notes            // Optional notes
);

public record HabitExecutionRes(
    Guid Id,                 // Execution ID
    Guid HabitVersionId,     // References habit version
    string Date,             // Execution date
    string Status,           // "Completed" or "Failed"
    string? CompletedAt,     // When completed (if applicable)
    string? Notes,           // Optional notes
    bool PaymentProcessed    // Penalty payment status
);

public record SearchHabitQuery(
  Guid? Id,
  string? UserId,
  string? Task,
  bool? Enabled,
  int? Limit,
  int? Skip);

public record SearchHabitExecutionQuery(
  Guid? Id,
  string? Date,
  int? Limit,
  int? Skip);
