using Domain.Charity;

namespace Domain.Configuration

{
    public record ConfigurationRecord
    {
      public required string Timezone { get; init; }
      public Guid DefaultCharityId { get; init; }
    }
    
    public record ConfigurationPrincipal
    {
      public required Guid Id { get; init; }
      public required string UserId { get; init; }
      public required ConfigurationRecord Record { get; init; }
    }
    
    // An update, plus the days its timezone change settled because they had already ended
    // in the new timezone (an eastward trip).
    public record ConfigurationUpdate
    {
      public required ConfigurationPrincipal Principal { get; init; }
      public required List<Check.SettledCheck> SettledChecks { get; init; }
    }

    public record Configuration
    {
      public required ConfigurationPrincipal  Principal { get; init; }
      public CharityPrincipal? Charity { get; init; }
    }
}
