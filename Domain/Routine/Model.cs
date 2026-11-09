using Domain.User;

namespace Domain.Routine;

public record RoutineRecord
{
  public required string Name { get; init; }
}

public record RoutinePrincipal
{
  public required Guid Id { get; init; }
  public required string UserId { get; init; }
  public required RoutineRecord Record { get; init; }
}

public record Routine
{
  public required RoutinePrincipal Principal { get; init; }
  public required UserPrincipal User { get; init; }
}

public record RoutineSearch
{
  public string? UserId { get; init; }
  public string? Name { get; init; }
  public required int Limit { get; init; }
  public required int Skip { get; init; }
}
