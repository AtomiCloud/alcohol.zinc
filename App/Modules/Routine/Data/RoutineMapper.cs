using Domain.Routine;
using Domain.User;

namespace App.Modules.Routine.Data;

public static class RoutineMapper
{
  public static RoutineData ToData(this RoutineRecord record, string userId)
  {
    return new RoutineData
    {
      UserId = userId,
      Name = record.Name
    };
  }

  public static RoutineData ToData(this RoutineData data, RoutineRecord record)
  {
    data.Name = record.Name;
    return data;
  }

  public static RoutineRecord ToRecord(this RoutineData data)
  {
    return new RoutineRecord
    {
      Name = data.Name
    };
  }

  public static RoutinePrincipal ToPrincipal(this RoutineData data)
  {
    return new RoutinePrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      Record = data.ToRecord()
    };
  }

  public static Domain.Routine.Routine ToDomain(this RoutineData data, UserPrincipal userPrincipal)
  {
    return new Domain.Routine.Routine
    {
      Principal = data.ToPrincipal(),
      User = userPrincipal
    };
  }
}
