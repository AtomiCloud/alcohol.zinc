using System.ComponentModel;
using System.Text.Json.Serialization;
using App.Modules.Common;

namespace App.Error.V1;

[Description(
  "This error occurs when attempt to user the user via JWT token and JWT is empty or does not have fields that is necessary for user creation")]
public class InvalidUserToken : IDomainProblem
{
  public InvalidUserToken() { }

  public InvalidUserToken(string detail, string tokenType, string[] missingClaims)
  {
    this.Detail = detail;
    this.MissingClaims = missingClaims;
    this.TokenType = tokenType;
  }

  [JsonIgnore] public string Id { get; } = "invalid_user_token";

  [JsonIgnore] public string Title { get; } = "Invalid User Token";

  [JsonIgnore] public string Version { get; } = "v1";

  public string Detail { get; } = string.Empty;

  [Description("All the claims that are expect from backend but not supplied by frontend")]
  public string[] MissingClaims { get; } = [];
  
  [Description("Type of token that was missing, either 'ID' or 'Access'")]
  public string TokenType { get; } = string.Empty;
}
