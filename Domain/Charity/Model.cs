namespace Domain.Charity
{
    public record CharitySearch
    {
      public string? Name { get; init; }
      public string? Slug { get; init; }
      public string? Country { get; init; }
      public string? PrimaryRegistrationNumber { get; init; }
      public string? PrimaryRegistrationCountry { get; init; }
      public string? CauseKey { get; init; }
      public bool? IsVerified { get; init; }
      public bool? DonationEnabled { get; init; }
      public int Limit { get; init; }
      public int Skip { get; init; }
    }

    public class CharityRecord
    {
      public required string Name { get; init; }
      public string? Slug { get; init; }
      public string? Mission { get; init; }
      public string? Description { get; init; }
      public string[]? Countries { get; init; }
      public string? PrimaryRegistrationNumber { get; init; }
      public string? PrimaryRegistrationCountry { get; init; }
      public string? WebsiteUrl { get; init; }
      public string? LogoUrl { get; init; }
      public bool? IsVerified { get; init; }
      public string? VerificationSource { get; init; }
      public DateTimeOffset? LastVerifiedAt { get; init; }
      public bool? DonationEnabled { get; init; }
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

    public record BulkUpsertCharity
    {
      public required CharityRecord Charity { get; init; }
      public required ExternalIdRecord ExternalId { get; init; }
      public required string[] CauseKeys { get; init; }
    }

    public record BulkUpsertResult
    {
      public required int CharitiesCreated { get; init; }
      public required int CharitiesUpdated { get; init; }
      public required int ExternalIdsLinked { get; init; }
      public required int CausesLinked { get; init; }
    }
}
