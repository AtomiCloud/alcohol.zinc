using CSharp_Result;

namespace App.StartUp.Services.Auth;

public interface IAuthManagement
{
  Task<Result<Unit>> AssignRole(string userId, string roleId);
  Task<Result<Unit>> RemoveRole(string userId, string roleId);
  
  Task<Result<Unit>> SetClaim(string userId, string claimKey, string claimValue);
  
  Task<Result<Unit>> RemoveClaim(string userId, string claimKey);
  
}
