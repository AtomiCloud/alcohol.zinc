namespace App.Modules.Users.API.V1;

public record SearchUserQuery(
  string? Id,
  string? Username,
  string? Email,
  bool? EmailVerified,
  bool? Active,
  int? Limit,
  int? Skip);

// REQ
public record CreateUserReq(string IdToken, string AccessToken);

public record UpdateUserReq(string IdToken, string AccessToken);

// RESP

public record UserPrincipalRes(string Id, string Username, string Email, bool EmailVerified, bool Active);

public record UserRes(UserPrincipalRes Principal);
