using Domain.Routine;

namespace App.Modules.Routine.API.V1;

public static class RoutineMapper
{
  public static RoutineRecord ToRecord(this CreateRoutineReq req) =>
    new() { Name = req.Name };

  public static RoutineRecord ToRecord(this UpdateRoutineReq req) =>
    new() { Name = req.Name };

  public static RoutineRes ToRes(this RoutinePrincipal p) =>
    new(p.Id, p.UserId, p.Record.Name);

  public static RoutineSearch ToDomain(this SearchRoutineQuery query, string userId) =>
    new()
    {
      UserId = userId,
      Name = query.Name,
      Limit = query.Limit ?? 20,
      Skip = query.Skip ?? 0
    };
}
