using Domain.Charity;
using Domain.Habit;
using Domain.User;
using NodaMoney;

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
                Version = data.Version,
                Record = new HabitRecord
                {
                    Task = data.Task,
                    DayOfWeek = data.DayOfWeek,
                    NotificationTime = data.NotificationTime,
                    Stake = new Money(data.StakeCents, Currency.FromCode("USD")),
                    Ratio = data.RatioBasisPoints / 1000m,
                    StartDate = data.StartDate,
                    EndDate = data.EndDate,
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
                Version = principal.Version,
                HabitId = principal.HabitId,
                UserId = principal.UserId
            };
        }

        public static Domain.Habit.Habit ToHabit(this HabitPrincipal habitPrincipal, UserPrincipal userPrincipal, 
          CharityPrincipal charityPrincipal)
        {
            return new Domain.Habit.Habit
            {
                Principal = habitPrincipal,
                User = userPrincipal,
                Charity = charityPrincipal
            };
        }
    }
}
