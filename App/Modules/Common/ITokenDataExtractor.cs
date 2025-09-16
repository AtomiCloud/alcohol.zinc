using CSharp_Result;

namespace App.Modules.Common;

public record UserToken(string Sub, string Username, string Email, bool EmailVerified, string[] Scopes);

public interface ITokenDataExtractor
{
  Task<Result<UserToken>> ExtractFromToken(string idToken, string accessToken);
}
