using Domain.Charity;

namespace App.Modules.Charities.API.V1;

public static class CharityMapper
{
  // RES
  public static CharityPrincipalRes ToRes(this CharityPrincipal charityPrincipal)
    => new(
      charityPrincipal.Id.ToString(),
      charityPrincipal.Record.Name,
      charityPrincipal.Record.Slug,
      charityPrincipal.Record.Mission,
      charityPrincipal.Record.Description,
      charityPrincipal.Record.Countries ?? [],
      charityPrincipal.Record.PrimaryRegistrationNumber,
      charityPrincipal.Record.PrimaryRegistrationCountry,
      charityPrincipal.Record.WebsiteUrl,
      charityPrincipal.Record.LogoUrl,
      charityPrincipal.Record.IsVerified,
      charityPrincipal.Record.VerificationSource,
      charityPrincipal.Record.LastVerifiedAt,
      charityPrincipal.Record.DonationEnabled);

  public static CharityRes ToRes(this Charity charity)
    => new(charity.Principal.ToRes());

  // REQ
  public static CharityRecord ToRecord(this CreateCharityReq req) =>
    new()
    {
      Name = req.Name,
      Slug = req.Slug,
      Mission = req.Mission,
      Description = req.Description,
      Countries = req.Countries,
      PrimaryRegistrationNumber = req.PrimaryRegistrationNumber,
      PrimaryRegistrationCountry = req.PrimaryRegistrationCountry,
      WebsiteUrl = req.WebsiteUrl,
      LogoUrl = req.LogoUrl,
      IsVerified = req.IsVerified,
      VerificationSource = req.VerificationSource,
      LastVerifiedAt = req.LastVerifiedAt,
      DonationEnabled = req.DonationEnabled
    };

  public static CharityRecord ToRecord(this UpdateCharityReq req) =>
    new()
    {
      Name = req.Name,
      Slug = req.Slug,
      Mission = req.Mission,
      Description = req.Description,
      Countries = req.Countries,
      PrimaryRegistrationNumber = req.PrimaryRegistrationNumber,
      PrimaryRegistrationCountry = req.PrimaryRegistrationCountry,
      WebsiteUrl = req.WebsiteUrl,
      LogoUrl = req.LogoUrl,
      IsVerified = req.IsVerified,
      VerificationSource = req.VerificationSource,
      LastVerifiedAt = req.LastVerifiedAt,
      DonationEnabled = req.DonationEnabled
    };

  public static CharitySearch ToDomain(this CharitySearchReq req) =>
    new()
    {
      Name = req.Name,
      Slug = req.Slug,
      Country = req.Country,
      PrimaryRegistrationNumber = req.PrimaryRegistrationNumber,
      PrimaryRegistrationCountry = req.PrimaryRegistrationCountry,
      CauseKey = req.CauseKey,
      IsVerified = req.IsVerified,
      DonationEnabled = req.DonationEnabled,
      Limit = req.Limit ?? 20,
      Skip = req.Skip ?? 0,
    };
}
