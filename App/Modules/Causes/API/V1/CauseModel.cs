namespace App.Modules.Causes.API.V1;

// REQ
public record CreateCauseReq(string Key, string Name);

public record UpdateCauseReq(string Name);

public record CauseSearchReq(string? Key, string? Name, int? Limit, int? Skip);

// RESP
public record CausePrincipalRes(string Id, string Key, string Name);

public record CauseRes(CausePrincipalRes Principal);
