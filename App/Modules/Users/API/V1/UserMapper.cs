using App.Modules.Common;
using Domain.User;

namespace App.Modules.Users.API.V1;

public static class UserMapper
{
  // RES
  public static UserPrincipalRes ToRes(this UserPrincipal userPrincipal)
    => new(userPrincipal.Id, userPrincipal.Record.Username, userPrincipal.Record.Email,
      userPrincipal.Record.EmailVerified, userPrincipal.Record.Active);

  public static UserRes ToRes(this User user)
    => new(user.Principal.ToRes());


  // REQ
  public static UserRecord ToRecord(this UserToken token) =>
    new()
    {
      Username = token.Username,
      Email = token.Email,
      EmailVerified = token.EmailVerified,
      Scopes = token.Scopes,
      Active = token.EmailVerified && token.Username.Length > 0 && token.Email.Length > 0,
    };


  public static UserSearch ToDomain(this SearchUserQuery query) =>
    new()
    {
      Id = query.Id, 
      Username = query.Username,
      Email = query.Email,
      EmailVerified = query.EmailVerified,
      Active = query.Active,
      Limit = query.Limit ?? 20, 
      Skip = query.Skip ?? 0,
    };
}
