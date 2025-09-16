using Domain.User;

namespace App.Modules.Users.Data;

public static class UserMapper
{
  public static UserRecord ToRecord(this UserData principal) => new()
  {
    Active = principal.Active,
    Username = principal.Username,
    Email = principal.Email,
    EmailVerified = principal.EmailVerified,
    Scopes = principal.Scopes,
  };

  public static UserPrincipal ToPrincipal(this UserData data) => new()
  {
    Id = data.Id, 
    Record = data.ToRecord(),
  };


  public static User ToDomain(this UserData data) => new()
  {
    Principal = data.ToPrincipal(),
  };

  public static UserData ToData(this UserRecord record) => new()
  {
    Username = record.Username,
    Active = record.Active,
    Email = record.Email,
    EmailVerified = record.EmailVerified,
    Scopes = record.Scopes,
  };

  public static UserData Update(this UserData data, UserRecord record)
  {
    data.Username = record.Username;
    data.Active = record.Active;
    data.Email = record.Email;
    data.EmailVerified = record.EmailVerified;
    data.Scopes = record.Scopes;
    return data;
  }
}
