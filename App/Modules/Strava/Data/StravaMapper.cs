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

  // Create. Ids and the assignment come from the database and the matcher.
  public static StravaActivityData ToData(this StravaActivityRecord record, string userId, long activityId,
    DateTime fetchedAtUtc) =>
    new StravaActivityData
    {
      ActivityId = activityId,
      UserId = userId,
      Name = record.Name,
      SportType = record.SportType,
      StartUtc = record.StartUtc,
      Manual = record.Manual,
      LocalDate = record.LocalDate,
      FetchedAtUtc = fetchedAtUtc
    }.ToData(record, fetchedAtUtc);

  // A re-fetch. Identity and the assignment stay.
  public static StravaActivityData ToData(this StravaActivityData data, StravaActivityRecord record,
    DateTime fetchedAtUtc)
  {
    data.Name = record.Name.Length > 256 ? record.Name[..256] : record.Name;
    data.SportType = record.SportType;
    data.StartUtc = DateTime.SpecifyKind(record.StartUtc, DateTimeKind.Utc);
    data.Manual = record.Manual;
    data.LocalDate = record.LocalDate;
    data.FetchedAtUtc = DateTime.SpecifyKind(fetchedAtUtc, DateTimeKind.Utc);
    return data;
  }

  public static StravaActivityRecord ToRecord(this StravaActivityData data) => new()
  {
    Name = data.Name,
    SportType = data.SportType,
    StartUtc = DateTime.SpecifyKind(data.StartUtc, DateTimeKind.Utc),
    Manual = data.Manual,
    LocalDate = data.LocalDate
  };

  public static StravaActivityPrincipal ToPrincipal(this StravaActivityData data) => new()
  {
    Id = data.Id,
    ActivityId = data.ActivityId,
    UserId = data.UserId,
    Record = data.ToRecord(),
    Assignment = new StravaAssignment(data.HabitId, data.Pinned)
  };
}
