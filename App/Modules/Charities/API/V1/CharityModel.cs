namespace App.Modules.Charities.API.V1;

// REQ
public record CreateCharityReq(string Name, string Email, string? Address);

public record UpdateCharityReq(string Name, string Email, string? Address);

// RESP
public record CharityPrincipalRes(string Id, string Name, string Email, string? Address);

public record CharityRes(CharityPrincipalRes Principal);