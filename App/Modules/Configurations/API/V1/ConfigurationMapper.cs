using App.Modules.Charities.API.V1;
using App.Utility;
using Domain.Check;
using Domain.Configuration;

namespace App.Modules.Configurations.API.V1;

public static class ConfigurationMapper
{
  // RES
  public static ConfigurationPrincipalRes ToRes(this ConfigurationPrincipal configPrincipal)
    => new(
        configPrincipal.Id.ToString(),
        configPrincipal.UserId,
        configPrincipal.Record.Timezone,
        configPrincipal.Record.DefaultCharityId.ToString()
      );

  public static UpdateConfigurationRes ToRes(this ConfigurationUpdate update)
    => new(
        update.Principal.Id.ToString(),
        update.Principal.UserId,
        update.Principal.Record.Timezone,
        update.Principal.Record.DefaultCharityId.ToString(),
        update.SettledChecks.Select(c => new TimezoneSettledCheckRes(
          c.HabitId.ToString(),
          c.LocalDate.ToStandardDateFormat(),
          c.Status switch
          {
            CheckStatus.Vacation => "vacation",
            CheckStatus.Frozen => "frozen",
            _ => "missed"
          })).ToList()
      );

  public static ConfigurationRes ToRes(this Configuration config)
    => new(
        config.Principal.ToRes(),
        config.Charity?.ToRes()  // Reuses charity mapper
      );

  // REQ
  public static ConfigurationRecord ToRecord(this CreateConfigurationReq req) =>
    new()
    {
      Timezone = req.Timezone,
      DefaultCharityId = Guid.Parse(req.DefaultCharityId)
    };

  public static ConfigurationRecord ToRecord(this UpdateConfigurationReq req) =>
    new()
    {
      Timezone = req.Timezone,
      DefaultCharityId = Guid.Parse(req.DefaultCharityId)
    };
}
