using System.Text.Json.Serialization;

namespace App.Modules.Charities.Sync;

public record PledgeCauseDto
{
  [JsonPropertyName("id")] public int Id { get; init; }
  [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
  [JsonPropertyName("parent_id")] public int? ParentId { get; init; }
}

public record PledgeCausesRes
{
  [JsonPropertyName("causes")] public PledgeCauseDto[] Causes { get; init; } = [];
}

public record PledgeOrganizationDto
{
  [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
  [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;

  [JsonPropertyName("alias")] public string Alias { get; init; } = string.Empty;

  [JsonPropertyName("ngo_id")] public string NgoId { get; init; } = string.Empty;
  [JsonPropertyName("mission")] public string Mission { get; init; } = string.Empty;

  [JsonPropertyName("street1")] public string Street1 { get; init; } = string.Empty;

  [JsonPropertyName("street2")] public string Street2 { get; init; } = string.Empty;

  [JsonPropertyName("city")] public string City { get; init; } = string.Empty;

  [JsonPropertyName("region")] public string Region { get; init; } = string.Empty;

  [JsonPropertyName("postal_code")] public string PostalCode { get; init; } = string.Empty;

  [JsonPropertyName("country")] public string Country { get; init; } = string.Empty;
  [JsonPropertyName("website_url")] public string WebsiteUrl { get; init; } = string.Empty;

  [JsonPropertyName("profile_url")] public string ProfileUrl { get; init; } = string.Empty;
  [JsonPropertyName("logo_url")] public string LogoUrl { get; init; } = string.Empty;

  [JsonPropertyName("disbursement_type")]
  public string DisbursementType { get; init; } = string.Empty;
}

public record PledgeOrganizationsPage
{
  [JsonPropertyName("page")] public int Page { get; init; }
  [JsonPropertyName("per")] public int PerPage { get; init; }
  [JsonPropertyName("total_count")] public int TotalCount { get; init; }

  [JsonPropertyName("uri")] public string Uri { get; init; } = string.Empty;
  [JsonPropertyName("next")] public string? NextUri { get; init; }
  [JsonPropertyName("previous")] public string? PreviousUri { get; init; }
  [JsonPropertyName("results")] public PledgeOrganizationDto[] Data { get; init; } = [];
}

// POST /v1/donations request body. email/first_name/last_name/amount/organization_id are
// required by Pledge; `amount` is a STRING DECIMAL in major units (e.g. "5.00"), and there is
// no currency field (Pledge donations bill the account's payment method on file). `metadata` is
// a free-form string — we store our Disbursement.Id there so a crashed attempt can be looked up.
public record PledgeDonationReq
{
  [JsonPropertyName("email")] public required string Email { get; init; }
  [JsonPropertyName("first_name")] public required string FirstName { get; init; }
  [JsonPropertyName("last_name")] public required string LastName { get; init; }
  [JsonPropertyName("amount")] public required string Amount { get; init; }
  [JsonPropertyName("organization_id")] public required string OrganizationId { get; init; }
  [JsonPropertyName("metadata")] public string? Metadata { get; init; }
}

public record PledgeDonationDto
{
  [JsonPropertyName("id")] public string Id { get; init; } = string.Empty;
  [JsonPropertyName("status")] public string Status { get; init; } = string.Empty;
  [JsonPropertyName("metadata")] public string? Metadata { get; init; }
  [JsonPropertyName("amount")] public string Amount { get; init; } = string.Empty;
  [JsonPropertyName("organization_id")] public string OrganizationId { get; init; } = string.Empty;
}

public record PledgeDonationsPage
{
  [JsonPropertyName("page")] public int Page { get; init; }
  [JsonPropertyName("per")] public int PerPage { get; init; }
  [JsonPropertyName("total_count")] public int TotalCount { get; init; }
  [JsonPropertyName("next")] public string? NextUri { get; init; }
  [JsonPropertyName("results")] public PledgeDonationDto[] Results { get; init; } = [];
}
