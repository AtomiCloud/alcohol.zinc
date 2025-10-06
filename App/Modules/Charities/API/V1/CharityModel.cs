namespace App.Modules.Charities.API.V1;

// REQ
public record CreateCharityReq(
  string Name,
  string? Slug,
  string? Mission,
  string[]? Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl);

public record UpdateCharityReq(
  string Name,
  string? Slug,
  string? Mission,
  string[]? Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl);

public record CharitySearchReq(
  string? Name,
  string? Slug,
  string? Country,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? CauseKey,
  int? Limit,
  int? Skip);

// RESP
public record CharityPrincipalRes(
  string Id,
  string Name,
  string? Slug,
  string? Mission,
  string[] Countries,
  string? PrimaryRegistrationNumber,
  string? PrimaryRegistrationCountry,
  string? WebsiteUrl,
  string? LogoUrl);

public record CharityRes(CharityPrincipalRes Principal);

// Linking
public record SetCharityCausesReq(string[] Keys);
