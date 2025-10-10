namespace Domain.Protection;

public record UserProtectionRecord
{
  public required int FreezeCurrent { get; init; }
}

public record UserProtectionPrincipal
{
  public required string UserId { get; init; }
  public required UserProtectionRecord Record { get; init; }
}

