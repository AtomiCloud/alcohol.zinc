using Domain.Habit;
using Domain.User;

namespace App.Modules.Habit.Data
{
    public static class HabitMapper
    {
        public static HabitPrincipal ToPrincipal(this HabitData data)
        {
            return new HabitPrincipal
            {
                Id = data.Id,
                UserId = data.UserId,
                Record = new HabitRecord
                {
                    Version = data.Version,
                    Enabled = data.Enabled
                }
            };
        }

        public static HabitData ToData(this HabitPrincipal principal)
        {
            return new HabitData
            {
                Id = principal.Id,
                UserId = principal.UserId,
                Version = principal.Record.Version,
                Enabled = principal.Record.Enabled
            };
        }

        public static Domain.Habit.Habit ToDomain(this HabitData data, UserPrincipal userPrincipal)
        {
            return new Domain.Habit.Habit
            {
                Principal = data.ToPrincipal(),
                User = userPrincipal
            };
        }
    }
}
