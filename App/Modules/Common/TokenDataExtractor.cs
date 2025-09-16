using System.Security.Claims;
using App.Error;
using App.Error.V1;
using CSharp_Result;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace App.Modules.Common;

public class TokenDataExtractor(
  IOptionsMonitor<JwtBearerOptions> jwtOptions,
  ILogger<TokenDataExtractor> logger
) : ITokenDataExtractor
{
  public async Task<Result<UserToken>> ExtractFromToken(string idToken, string accessToken)
  {
    if (string.IsNullOrWhiteSpace(idToken))
      return new DomainProblemException(new InvalidUserToken("Missing IdToken", "ID", []));
    if (string.IsNullOrWhiteSpace(accessToken))
      return new DomainProblemException(new InvalidUserToken("Missing AccessToken", "Access", []));

    var opt = jwtOptions.Get(JwtBearerDefaults.AuthenticationScheme);
    var handler = opt.TokenHandlers[0];

    try
    {
      // Get the signing keys from the OIDC discovery document
      var validationParameters = await GetValidationParametersWithKeysAsync(opt);

      var at = await handler.ValidateTokenAsync(accessToken, validationParameters);
      if (!at.IsValid)
      {
        var ex = new DomainProblemException(new InvalidUserToken($"Token validation failed: {at.Exception.Message}",
          "Access", []));
        logger.LogError(at.Exception, "Token validation failed: {Message}", ex.Message);
        return ex;
      }
      var accessTokenPrincipal = at.ClaimsIdentity;

      var itValidationParameters = validationParameters.Clone();
      itValidationParameters.ValidateAudience = false;
      var it = await handler.ValidateTokenAsync(idToken, itValidationParameters);
      if (!it.IsValid)
      {
        var ex = new DomainProblemException(new InvalidUserToken($"Token validation failed: {it.Exception.Message}",
          "ID", []));
        logger.LogError(it.Exception, "Token validation failed: {Message}", ex.Message);
        return ex;
      }

      var idTokenPrincipal = it.ClaimsIdentity;

      var idTokenSubClaim = idTokenPrincipal.Claims.FirstOrDefault(c => c.Type is ClaimTypes.NameIdentifier or "sub");
      var accessTokenSubClaim =
        accessTokenPrincipal.Claims.FirstOrDefault(c => c.Type is ClaimTypes.NameIdentifier or "sub");
      var usernameClaim = idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "username");
      var emailClaim =
        idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Email)
        ?? idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "email");
      var emailVerifiedClaim =
        idTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "email_verified");
      var scopesClaim = accessTokenPrincipal.Claims.FirstOrDefault(c => c.Type == "scope");

      if (idTokenSubClaim == null || accessTokenSubClaim == null || emailClaim == null || emailVerifiedClaim == null ||
          usernameClaim == null)
      {
        var fields = new List<string>();
        if (idTokenSubClaim == null) fields.Add("sub (ID token)");
        if (accessTokenSubClaim == null) fields.Add("sub (access token)");
        if (emailClaim == null) fields.Add("email");
        if (emailVerifiedClaim == null) fields.Add("email_verified");
        if (usernameClaim == null) fields.Add("username");
        logger.LogError("Missing fields in token: {@Fields}", fields);
        return new DomainProblemException(new InvalidUserToken("Fields missing", "ID", [..fields]));
      }

      // Validate that both tokens have the same subject
      if (idTokenSubClaim.Value != accessTokenSubClaim.Value)
      {
        logger.LogError("Token subject mismatch: ID token sub != Access token sub");
        return new DomainProblemException(new InvalidUserToken("ID and Access Tokens subject mismatch", "ID", []));
      }

      var sub = idTokenSubClaim.Value;
      var username = usernameClaim.Value;
      var email = emailClaim.Value;
      var emailVerified = emailVerifiedClaim.Value == "true";
      var scopes = scopesClaim?.Value.Split(" ") ?? [];

      var tokenData = new UserToken(sub, username, email, emailVerified, scopes);

      logger.LogInformation(
        "Successfully extracted data from tokens: HasUsername={HasUsername}, HasEmail={HasEmail}, EmailVerified={EmailVerified}, Scopes={ScopeCount}",
        !string.IsNullOrEmpty(username),
        !string.IsNullOrEmpty(email),
        emailVerified,
        scopes.Length
      );

      return tokenData;
    }

    catch (Exception ex)
    {
      logger.LogError(ex, "Unexpected error during token validation: {Message}", ex.Message);
      throw;
    }
  }

  private async Task<TokenValidationParameters> GetValidationParametersWithKeysAsync(JwtBearerOptions options)
  {
    var validationParameters = options.TokenValidationParameters;

    // Only fetch signing keys if they're not already set
    if (validationParameters.IssuerSigningKeys == null || !validationParameters.IssuerSigningKeys.Any())
    {
      try
      {
        // Create configuration manager to retrieve OIDC discovery document
        var configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
          $"{options.Authority}/.well-known/openid-configuration",
          new OpenIdConnectConfigurationRetriever(),
          new HttpDocumentRetriever { RequireHttps = options.RequireHttpsMetadata }
        );

        // Get the OIDC configuration which includes signing keys
        var configuration = await configurationManager.GetConfigurationAsync(CancellationToken.None);

        // Set the signing keys from the discovery document
        validationParameters.IssuerSigningKeys = configuration.SigningKeys;

        logger.LogDebug("Successfully retrieved {KeyCount} signing keys from OIDC discovery endpoint",
          configuration.SigningKeys.Count);
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "Failed to retrieve signing keys from OIDC discovery endpoint: {Authority}", options.Authority);
        throw;
      }
    }

    return validationParameters;
  }
}
