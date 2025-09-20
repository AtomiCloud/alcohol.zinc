using System.Globalization;
using App.Utility;
using Domain.Habit;
using NodaMoney;

namespace App.Modules.Habit.API.V1;

public static class HabitMapper
{
    public static HabitRes ToRes(this HabitPrincipal h) =>
        new (
            h.Id,
            h.Record.Version,
            h.UserId,
            h.Record.Enabled
        );

    public static HabitVersionRes ToRes(this HabitVersionPrincipal hv) =>
        new (
            hv.Id,
            hv.HabitId,
            hv.Record.Version,
            hv.Record.Task,
            hv.Record.DaysOfWeek,
            hv.Record.NotificationTime.ToStandardTimeFormat(),
            hv.Record.Stake.Amount.ToString("F2", CultureInfo.InvariantCulture),
            (hv.Record.Ratio * 100m).ToString("F1", CultureInfo.InvariantCulture),
            hv.Record.CharityId
        );

    public static HabitVersionRecord ToVersionRecord(this CreateHabitReq req) =>
        new ()
        {
            CharityId = req.CharityId,
            Task = req.Task,
            DaysOfWeek = req.DaysOfWeek,
            NotificationTime = req.NotificationTime.ToTime(),
            Stake = new Money(decimal.Parse(req.Stake, CultureInfo.InvariantCulture), Currency.FromCode("USD")),
            Ratio = 1.0m,  // Fixed at 100% - all stake goes to charity
            Version = 1  // First version
        };

    public static HabitVersionRecord ToVersionRecord(this UpdateHabitReq req, ushort version) =>
        new ()
        {
            CharityId = req.CharityId,
            Task = req.Task,
            DaysOfWeek = req.DaysOfWeek,
            NotificationTime = req.NotificationTime.ToTime(),
            Stake = new Money(decimal.Parse(req.Stake, CultureInfo.InvariantCulture), Currency.FromCode("USD")),
            Ratio = 1.0m,  // Fixed at 100% - all stake goes to charity
            Version = version  // Will be set by repository
        };

    public static HabitExecutionRes ToRes(this HabitExecutionPrincipal he) =>
        new (
            he.Id,
            he.HabitVersionId,
            he.Record.Date.ToStandardDateFormat(),
            he.Record.Status.ToString(),
            he.Record.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
            he.Record.Notes,
            false  // PaymentProcessed - not exposed in domain model yet
        );
}
