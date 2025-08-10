using System.Globalization;
using App.Utility;
using Domain.Habit;
using NodaMoney;

namespace App.Modules.Habit.API.V1;

public static class HabitMapper
{
    public static HabitRes ToRes(this HabitPrincipal h) =>
        new (
            h.Id.ToString(),
            h.Record.Task,
            h.Record.DayOfWeek,
            h.Record.NotificationTime.ToStandardTimeFormat(),
            h.Record.Stake.Amount.ToString("F2", CultureInfo.InvariantCulture),
            (h.Record.Ratio * 100m).ToString("F1", CultureInfo.InvariantCulture),
            h.Record.StartDate.ToStandardDateFormat(),
            h.Record.EndDate.ToStandardDateFormat(),
            h.CharityId,
            h.Version,
            h.UserId,
            h.HabitId.ToString()
        );

    public static HabitRecord ToRecord(this CreateHabitReq req) =>
        new ()
        {
            Task = req.Task,
            DayOfWeek = req.DayOfWeek,
            NotificationTime = req.NotificationTime.ToTime(),
            Stake = new Money(decimal.Parse(req.Stake, CultureInfo.InvariantCulture), Currency.FromCode("SGD")),
            Ratio = decimal.Parse(req.Ratio, CultureInfo.InvariantCulture) / 100m,
            StartDate = req.StartDate.ToDate(),
            EndDate = req.EndDate.ToDate(),
        };

    public static HabitPrincipal ToPrincipal(this UpdateHabitReq req, Guid id) =>
        new ()
        {
            Id = id,
            UserId = req.UserId,
            HabitId = req.HabitId,
            CharityId = req.CharityId,
            Version = req.Version,
            Record = new HabitRecord
            {
                Task = req.Task,
                DayOfWeek = req.DayOfWeek,
                NotificationTime = req.NotificationTime.ToTime(),
                Stake = new Money(decimal.Parse(req.Stake, CultureInfo.InvariantCulture), Currency.FromCode("SGD")),
                Ratio = decimal.Parse(req.Ratio, CultureInfo.InvariantCulture) / 100m,
                StartDate = req.StartDate.ToDate(),
                EndDate = req.EndDate.ToDate(),
            }
        };
}
