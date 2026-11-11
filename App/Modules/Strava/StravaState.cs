using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace App.Modules.Strava;

// The OAuth state for the connect flow: which user started it and until when, HMAC-signed so
// the anonymous callback can't be pointed at another user's account.
public static class StravaState
{
  public static string Sign(string userId, DateTime expiresAtUtc, string key)
  {
    var payload = $"{new DateTimeOffset(expiresAtUtc).ToUnixTimeSeconds()}:{userId}";
    var body = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
    return $"{body}.{Mac(body, key)}";
  }

  // The user ID, or null when the state is forged, malformed or expired.
  public static string? Verify(string? state, DateTime nowUtc, string key)
  {
    var parts = state?.Split('.');
    if (parts is not [var body, var mac]) return null;
    if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Mac(body, key)), Encoding.ASCII.GetBytes(mac)))
      return null;

    string payload;
    try
    {
      payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(body));
    }
    catch (FormatException)
    {
      return null;
    }

    var sep = payload.IndexOf(':');
    if (sep <= 0 || !long.TryParse(payload[..sep], out var expires)) return null;
    if (DateTimeOffset.FromUnixTimeSeconds(expires).UtcDateTime < nowUtc) return null;
    var userId = payload[(sep + 1)..];
    return userId.Length == 0 ? null : userId;
  }

  private static string Mac(string body, string key) =>
    WebEncoders.Base64UrlEncode(HMACSHA256.HashData(Encoding.UTF8.GetBytes(key), Encoding.UTF8.GetBytes(body)));
}
