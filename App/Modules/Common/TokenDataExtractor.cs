using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using App.Error;
using App.Error.V1;
using App.StartUp.Options.Auth;
using CSharp_Result;
using Microsoft.Extensions.Options;

namespace App.Modules.Common;

public class TokenDataExtractor(
  IOptionsMonitor<AuthOption> authOption,
  ILogger<TokenDataExtractor> logger
) : ITokenDataExtractor
{
  public async Task<Result<UserToken>> ExtractFromToken(string idToken, string accessToken)
  {
    await Task.CompletedTask;
    if (string.IsNullOrWhiteSpace(idToken))
      return new DomainProblemException(new InvalidUserToken("Missing IdToken", "ID", []));
    if (string.IsNullOrWhiteSpace(accessToken))
      return new DomainProblemException(new InvalidUserToken("Missing AccessToken", "Access", []));

    var authSettings = authOption.CurrentValue.Settings;
    if (authSettings?.TokenValidation == null)
    {
      var ex = new ApplicationException("Auth settings or token validation not configured");
      logger.LogError(ex, "Auth settings or token validation not configured");
      throw ex;
    }

    var handler = new JwtSecurityTokenHandler();

    // Extract email and email_verified from ID token
    var idTokenParsed = handler.ReadJwtToken(idToken);
    var accessTokenParsed = handler.ReadJwtToken(accessToken);

    var usernameClaim = idTokenParsed.Claims.FirstOrDefault(c => c.Type == "username");
    var emailClaim =
      idTokenParsed.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)
      ?? idTokenParsed.Claims.FirstOrDefault(c => c.Type == "email");
    var emailVerifiedClaim =
      idTokenParsed.Claims.FirstOrDefault(c => c.Type == "email_verified");
    var scopesClaim = accessTokenParsed.Claims.FirstOrDefault(c => c.Type == "scope");
    if (emailClaim == null || emailVerifiedClaim == null || usernameClaim == null)
    {
      var fields = new List<string>();
      if (emailClaim == null) fields.Add("email");
      if (emailVerifiedClaim == null) fields.Add("email_verified");
      if (usernameClaim == null) fields.Add("username");
      logger.LogError("Missing fields in token: {@Fields}", fields);
      return new DomainProblemException(new InvalidUserToken("Fields missing", "ID", [..fields]));
    }

    var username = usernameClaim.Value;
    var email = emailClaim.Value;
    var emailVerified = emailVerifiedClaim.Value == "true";
    var scopes = scopesClaim?.Value.Split(" ") ?? [];

    var tokenData = new UserToken(username, email, emailVerified, scopes);

    logger.LogInformation(
      "Successfully extracted data from tokens: Username={Username} Email={HasEmail}, EmailVerified={EmailVerified}, Scopes={RoleCount}",
      username,
      email,
      emailVerified,
      scopes.Length
    );

    return tokenData;
  }
}
