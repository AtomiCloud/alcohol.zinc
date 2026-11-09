namespace App.Modules.Routine.API.V1;

public record CreateRoutineReq(
  string Name
);

public record UpdateRoutineReq(
  string Name
);

public record RoutineRes(
  Guid Id,
  string UserId,
  string Name
);

public record SearchRoutineQuery(
  string? Name,
  int? Limit,
  int? Skip
);
