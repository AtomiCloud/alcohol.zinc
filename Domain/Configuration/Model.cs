using Domain.Charity;

namespace Domain.Configuration

{
    public record ConfigurationRecord
    {
      public required string Timezone { get; init; }
      public TimeOnly EndOfDay { get; init; }
      public Guid DefaultCharityId { get; init; }
    }
    
    public record ConfigurationPrincipal
    {
      public required Guid Id { get; init; }
      public required string UserId { get; init; }
      public required ConfigurationRecord Record { get; init; }
    }
    
    public record Configuration
    {
      public required ConfigurationPrincipal  Principal { get; init; }
      public CharityPrincipal? Charity { get; init; }
    }
}
