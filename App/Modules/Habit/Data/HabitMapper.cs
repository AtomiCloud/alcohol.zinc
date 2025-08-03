using Domain.Habit;
using Domain.Charity;
using Domain.User;

namespace App.Modules.Habit.Data
{
    public static class HabitMapper
    {
        public static HabitPrincipal ToPrincipal(this HabitData data)
        {
            return new HabitPrincipal
            {
                Id = data.Id ?? Guid.NewGuid(),
                UserId = data.UserId,
                HabitId = data.HabitId,
                CharityId = data.CharityId,
                Record = new HabitRecord
                {
                    Task = data.Task,
                    DayOfWeek = data.DayOfWeek,
                    NotificationTime = data.NotificationTime,
                    Stake = new NodaMoney.Money(data.StakeCents, NodaMoney.Currency.FromCode("USD")),
                    Ratio = data.RatioBasisPoints / 1000m,
                    StartDate = data.StartDate,
                    EndDate = data.EndDate,
                    Version = data.Version
                }
            };
        }

        public static HabitData ToData(this HabitPrincipal principal)
        {
            return new HabitData
            {
                Id = principal.Id,
                Task = principal.Record.Task,
                DayOfWeek = principal.Record.DayOfWeek,
                NotificationTime = principal.Record.NotificationTime,
                StakeCents = (int)(principal.Record.Stake.Amount / principal.Record.Stake.Currency.MinimalAmount),
                RatioBasisPoints = (int)(principal.Record.Ratio * 1000m),
                StartDate = principal.Record.StartDate,
                EndDate = principal.Record.EndDate,
                CharityId = principal.CharityId,
                Version = principal.Record.Version,
                HabitId = principal.HabitId,
                UserId = principal.UserId
            };
        }

        public static Domain.Habit.Habit ToHabit(this HabitPrincipal principal, UserPrincipal user, CharityModel? charity = null)
        {
            return new Domain.Habit.Habit
            {
                Principal = principal,
                User = user,
                Charity = charity
            };
        }
    }
}
