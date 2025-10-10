using CSharp_Result;

namespace Domain.Vacation;

public record VacationRecord
{
  public required DateOnly StartDate { get; init; }
  public required DateOnly EndDate { get; init; }
  public required string Timezone { get; init; }
}

public record VacationPrincipal
{
  public required Guid Id { get; init; }
  public required string UserId { get; init; }
  public required VacationRecord Record { get; init; }
}

public record Vacation
{
  public required VacationPrincipal Principal { get; init; }
}

public record VacationSearch
{
  public required string UserId { get; init; }
  public int? Year { get; init; }
  public required int Limit { get; init; }
  public required int Skip { get; init; }
}

