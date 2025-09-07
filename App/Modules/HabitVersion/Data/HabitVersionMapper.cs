using Domain.Habit;
using NodaMoney;

namespace App.Modules.HabitVersion.Data
{
    public static class HabitVersionMapper
    {
        public static HabitVersionPrincipal ToPrincipal(this HabitVersionData data)
        {
            return new HabitVersionPrincipal
            {
                Id = data.Id,
                HabitId = data.HabitId,
                Record = new HabitVersionRecord
                {
                    CharityId = data.CharityId,
                    Task = data.Task,
                    DayOfWeek = data.DayOfWeek,
                    NotificationTime = data.NotificationTime,
                    Stake = new Money(data.StakeCents / 100m, Currency.FromCode(data.StakeCurrency)),
                    Ratio = data.RatioBasisPoints / 10000m,  // Basis points to decimal
                    StartDate = data.StartDate,
                    EndDate = data.EndDate,
                    Version = data.Version
                }
            };
        }

        public static HabitVersionData ToData(this HabitVersionPrincipal principal)
        {
            return new HabitVersionData
            {
                Id = principal.Id,
                HabitId = principal.HabitId,
                CharityId = principal.Record.CharityId,
                Version = principal.Record.Version,
                Task = principal.Record.Task,
                DayOfWeek = principal.Record.DayOfWeek,
                NotificationTime = principal.Record.NotificationTime,
                StakeCents = (int)(principal.Record.Stake.Amount * 100),  // Convert to cents
                StakeCurrency = principal.Record.Stake.Currency.Code,
                RatioBasisPoints = (int)(principal.Record.Ratio * 10000),  // Convert to basis points
                StartDate = principal.Record.StartDate,
                EndDate = principal.Record.EndDate
            };
        }

        public static HabitVersionData ToData(this HabitVersionRecord record, Guid habitId, ushort version)
        {
            return new HabitVersionData
            {
                Id = Guid.NewGuid(),
                HabitId = habitId,
                CharityId = record.CharityId,
                Version = version,
                Task = record.Task,
                DayOfWeek = record.DayOfWeek,
                NotificationTime = record.NotificationTime,
                StakeCents = (int)(record.Stake.Amount * 100),  // Convert to cents
                StakeCurrency = record.Stake.Currency.Code,
                RatioBasisPoints = (int)(record.Ratio * 10000),  // Convert to basis points
                StartDate = record.StartDate,
                EndDate = record.EndDate
            };
        }
    }
}
