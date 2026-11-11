using App.Modules.System;
using Domain.Strava;

namespace App.Modules.Strava.Data;

public static class StravaMapper
{
  public static StravaConnectionData ToData(this StravaConnectionRecord record, string userId, IEncryptor encryptor)
  {
    return new StravaConnectionData
    {
      UserId = userId,
      AthleteId = record.AthleteId,
      AccessToken = encryptor.Encrypt(record.Tokens.AccessToken),
      RefreshToken = encryptor.Encrypt(record.Tokens.RefreshToken),
      ExpiresAtUtc = record.Tokens.ExpiresAtUtc,
      Scope = record.Scope
    };
  }

  public static StravaConnectionData ToData(this StravaConnectionData data, StravaConnectionRecord record,
    IEncryptor encryptor)
  {
    data.AthleteId = record.AthleteId;
    data.Scope = record.Scope;
    return data.ToData(record.Tokens, encryptor);
  }

  public static StravaConnectionData ToData(this StravaConnectionData data, StravaTokens tokens, IEncryptor encryptor)
  {
    data.AccessToken = encryptor.Encrypt(tokens.AccessToken);
    data.RefreshToken = encryptor.Encrypt(tokens.RefreshToken);
    data.ExpiresAtUtc = tokens.ExpiresAtUtc;
    data.UpdatedAt = DateTime.UtcNow;
    return data;
  }

  public static StravaConnectionRecord ToRecord(this StravaConnectionData data, IEncryptor encryptor)
  {
    return new StravaConnectionRecord
    {
      AthleteId = data.AthleteId,
      Tokens = new StravaTokens(encryptor.Decrypt(data.AccessToken), encryptor.Decrypt(data.RefreshToken),
        DateTime.SpecifyKind(data.ExpiresAtUtc, DateTimeKind.Utc)),
      Scope = data.Scope
    };
  }

  public static StravaConnectionPrincipal ToPrincipal(this StravaConnectionData data, IEncryptor encryptor)
  {
    return new StravaConnectionPrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      ConnectedAtUtc = DateTime.SpecifyKind(data.ConnectedAtUtc, DateTimeKind.Utc),
      Record = data.ToRecord(encryptor)
    };
  }

  public static StravaEventData ToData(this StravaWebhookEvent e)
  {
    return new StravaEventData
    {
      ObjectType = e.ObjectType,
      AspectType = e.AspectType,
      ObjectId = e.ObjectId,
      OwnerId = e.OwnerId,
      EventTimeUtc = e.EventTimeUtc,
      Deauthorized = e.Deauthorized
    };
  }

  public static StravaWebhookEvent ToRecord(this StravaEventData data)
  {
    return new StravaWebhookEvent
    {
      ObjectType = data.ObjectType,
      AspectType = data.AspectType,
      ObjectId = data.ObjectId,
      OwnerId = data.OwnerId,
      EventTimeUtc = DateTime.SpecifyKind(data.EventTimeUtc, DateTimeKind.Utc),
      Deauthorized = data.Deauthorized
    };
  }

  public static StravaInboxItem ToPrincipal(this StravaEventData data) => new(data.Id, data.Attempts, data.ToRecord());
}
