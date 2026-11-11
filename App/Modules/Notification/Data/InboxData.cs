using System.ComponentModel.DataAnnotations;
using App.Modules.Users.Data;

namespace App.Modules.Notification.Data;

// One in-app notification. Deleted with its recipient's or its actor's account.
public class InboxNotificationData
{
  [Key]
  public Guid Id { get; set; }

  [MaxLength(128)]
  public required string UserId { get; set; }
  public virtual UserData? User { get; set; }

  [MaxLength(32)]
  public required string Kind { get; set; }

  [MaxLength(256)]
  public required string Title { get; set; }

  [MaxLength(1024)]
  public required string Body { get; set; }

  [MaxLength(128)]
  public string? ActorUserId { get; set; }
  public virtual UserData? Actor { get; set; }

  [MaxLength(2048)]
  public string? Data { get; set; }

  [MaxLength(128)]
  public string? DedupeKey { get; set; }

  public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
  public DateTime? ReadAtUtc { get; set; }
}
