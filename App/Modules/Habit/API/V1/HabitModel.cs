public record CreateHabitReq(
    string Task,
    string[] DaysOfWeek,
    string NotificationTime, // string (e.g. "HH:mm")
    string Stake,            // string decimal (e.g. "10.50")
    Guid? CharityId,         // Needed only when Stake > 0; null or empty: no charity
    string Timezone,         // string (e.g. "Asia/Singapore", "America/New_York")
    string? DeadlineTime = null, // "HH:mm", the day's check deadline; defaults to 23:59
    Guid? RoutineId = null   // Optional: existing routine of the user; omitted => new one-habit routine
);

public record UpdateHabitReq(
    string Task,
    string[] DaysOfWeek,
    string NotificationTime,
    string Stake,
    Guid? CharityId,         // Needed only when Stake > 0; null or empty: no charity
    bool Enabled,
    string Timezone,         // string (e.g. "Asia/Singapore", "America/New_York")
    string? DeadlineTime = null  // "HH:mm", the day's check deadline; defaults to 23:59
);

public record HabitRes(
    Guid Id,                 // Habit ID (main entity)
    ushort Version,          // Current version number
    string UserId,
    bool Enabled,            // Whether habit is enabled
    Guid? RoutineId          // Owning routine (null for habits created before routines existed)
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
    Guid? CharityId,         // Null for a habit without a money stake
    string Timezone,         // string (e.g. "Asia/Singapore", "America/New_York")
    string DeadlineTime      // "HH:mm"
);

public record MarkDailyFailuresCronRes(
    int TotalMarked
);

public record CompleteHabitReq(
    string? Notes            // Optional notes
);

public record SkipHabitReq(
    string? Notes
);

public record HabitExecutionRes(
    Guid Id,                 // Execution ID
    Guid HabitVersionId,     // References habit version
    string Date,             // Execution date
    string Status,           // one of: "succeeded", "failed", "vacation", "skip", "frozen"
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
