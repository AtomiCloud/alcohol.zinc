using Domain.Habit;
using Domain.User;

namespace App.Modules.Habit.Data
{
    public static class HabitMapper
    {
        public static HabitRecord ToRecord(this HabitData data)
        {
            return new HabitRecord
            {
                Version = data.Version,
                SortOrder = data.SortOrder,
                Enabled = data.Enabled,
                RoutineId = data.RoutineId
            };
        }

        public static HabitPrincipal ToPrincipal(this HabitData data)
        {
            return new HabitPrincipal
            {
                Id = data.Id,
                UserId = data.UserId,
                Record = data.ToRecord()
            };
        }

        public static HabitData ToData(this HabitPrincipal principal)
        {
            return new HabitData
            {
                Id = principal.Id,
                UserId = principal.UserId,
                Version = principal.Record.Version,
                Enabled = principal.Record.Enabled,
                RoutineId = principal.Record.RoutineId
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
