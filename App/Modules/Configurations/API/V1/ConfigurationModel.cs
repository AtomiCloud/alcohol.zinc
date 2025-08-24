using App.Modules.Charities.API.V1;

namespace App.Modules.Configurations.API.V1;

// REQ
public record CreateConfigurationReq(string Timezone, string EndOfDay, string DefaultCharityId);

public record UpdateConfigurationReq(string Timezone, string EndOfDay, string DefaultCharityId);

// RESP
public record ConfigurationPrincipalRes(string Id, string UserId, string Timezone, string EndOfDay, string DefaultCharityId);

public record ConfigurationRes(ConfigurationPrincipalRes Principal, CharityPrincipalRes? Charity);