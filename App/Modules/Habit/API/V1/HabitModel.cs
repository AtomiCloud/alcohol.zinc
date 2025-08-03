public record CreateHabitReq(
    string Task,
    string DayOfWeek,
    string NotificationTime, // string (e.g. "HH:mm")
    string Stake,            // string decimal
    string Ratio,            // string decimal
    string StartDate,        // string (e.g. "yyyy-MM-dd")
    string EndDate,          // string (e.g. "yyyy-MM-dd")
    int? CharityId,
    int Version,
    string UserId
);

public record UpdateHabitReq(
    string Task,
    string DayOfWeek,
    string NotificationTime,
    string Stake,
    string Ratio,
    string StartDate,
    string EndDate,
    int? CharityId,
    int Version,
    string UserId,
    Guid HabitId
);

public record HabitRes(
    string? Id,
    string Task,
    string DayOfWeek,
    string NotificationTime,
    string Stake,
    string Ratio,
    string StartDate,
    string EndDate,
    int? CharityId,
    int Version,
    string UserId,
    string HabitId
);
