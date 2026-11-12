using App.Modules.Charities.API.V1;

namespace App.Modules.Configurations.API.V1;

// REQ
// DefaultCharityId is optional (null or omitted: none); only staked habits need a charity.
// An update without it clears the default.
public record CreateConfigurationReq(string Timezone, string? DefaultCharityId = null);

public record UpdateConfigurationReq(string Timezone, string? DefaultCharityId = null);

// RESP
public record ConfigurationPrincipalRes(string Id, string UserId, string Timezone, string? DefaultCharityId);

// The principal's fields, plus the days the timezone change settled (status: missed, frozen
// or vacation). Empty unless the timezone moved east past the end of a pending day.
public record UpdateConfigurationRes(string Id, string UserId, string Timezone, string? DefaultCharityId,
  List<TimezoneSettledCheckRes> SettledChecks);

public record TimezoneSettledCheckRes(string HabitId, string Date, string Status);

public record ConfigurationRes(ConfigurationPrincipalRes Principal, CharityPrincipalRes? Charity);