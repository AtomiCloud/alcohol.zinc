namespace Domain.Charity
{
    public class CharityRecord
    {
      public required string Name { get; init; }
      public required string Email { get; init; }
      public string? Address { get; init; }
    }
    
    public class CharityPrincipal
    {
      public required Guid Id { get; init; }
      public required CharityRecord Record { get; init; }
    }

    public class Charity
    {
      public required CharityPrincipal Principal { get; init; }
    }
}
