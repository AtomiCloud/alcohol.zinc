using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Friend.Data;

// A pending request from FromUserId to ToUserId. Accepting or declining deletes it.
public class FriendRequestData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string FromUserId { get; set; }
  public virtual UserData? From { get; set; }

  [MaxLength(128)]
  public required string ToUserId { get; set; }
  public virtual UserData? To { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// One row per direction: a friendship between A and B is (A, B) and (B, A).
public class FriendshipData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }

  [MaxLength(128)]
  public required string FriendUserId { get; set; }
  public virtual UserData? FriendUser { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// A user's permanent invite link (one per user, unique on UserId); it never expires.
public class FriendInviteData
{
  [Key, MaxLength(32)]
  public required string Code { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
