namespace App.Modules.Charities.API.V1;

// REQ
public record CreateCharityReq(
  string Name,
  string? Slug,
  string? Mission,
  string? Description,
  string[]? Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl,
  bool? IsVerified,
  string? VerificationSource,
  DateTimeOffset? LastVerifiedAt,
  bool? DonationEnabled);

public record UpdateCharityReq(
  string Name,
  string? Slug,
  string? Mission,
  string? Description,
  string[]? Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl,
  bool? IsVerified,
  string? VerificationSource,
  DateTimeOffset? LastVerifiedAt,
  bool? DonationEnabled);

public record CharitySearchReq(
  string? Name,
  string? Slug,
  string? Country,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? CauseKey,
  bool? IsVerified,
  bool? DonationEnabled,
  int? Limit,
  int? Skip);

// RESP
public record CharityPrincipalRes(
  string Id,
  string Name,
  string? Slug,
  string? Mission,
  string? Description,
  string[] Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl,
  bool? IsVerified,
  string? VerificationSource,
  DateTimeOffset? LastVerifiedAt,
  bool? DonationEnabled);

public record CharityRes(CharityPrincipalRes Principal);

// Linking
public record SetCharityCausesReq(string[] Keys);
