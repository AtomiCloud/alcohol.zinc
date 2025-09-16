using System.Text.Json.Serialization;

namespace App.StartUp.Services.Auth.Logto;

public class LogtoAssignRoleReq
{
  [JsonPropertyName("roleIds")] public string[] RoleIds { get; set; }
}

public record LogtoTokenRes
{
  [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;

  [JsonPropertyName("expires_in")] public long ExpiresIn { get; set; }

  [JsonPropertyName("token_type")] public string TokenType { get; set; } = string.Empty;

  [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
}

public record ClaimsPatchReq
{
  [JsonPropertyName("customData")] public Dictionary<string, string?> CustomData { get; set; } = [];
};

public record LogtoAuthenticatorToken(string Secret, DateTime Expiry);
