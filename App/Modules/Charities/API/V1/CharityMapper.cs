using Domain.Charity;

namespace App.Modules.Charities.API.V1;

public static class CharityMapper
{
  // RES
  public static CharityPrincipalRes ToRes(this CharityPrincipal charityPrincipal)
    => new(charityPrincipal.Id.ToString(), charityPrincipal.Record.Name, charityPrincipal.Record.Email, charityPrincipal.Record.Address);

  public static CharityRes ToRes(this Charity charity)
    => new(charity.Principal.ToRes());

  // REQ
  public static CharityRecord ToRecord(this CreateCharityReq req) =>
    new() { Name = req.Name, Email = req.Email, Address = req.Address };

  public static CharityRecord ToRecord(this UpdateCharityReq req) =>
    new() { Name = req.Name, Email = req.Email, Address = req.Address };
}