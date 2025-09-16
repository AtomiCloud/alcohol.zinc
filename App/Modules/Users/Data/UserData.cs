using System.ComponentModel.DataAnnotations;

namespace App.Modules.Users.Data;

public class UserData
{
  // JWT Sub
  [MaxLength(128)] public string Id { get; set; } = string.Empty;

  // Custom Username
  [MaxLength(256)] public string Username { get; set; } = string.Empty;

  // Email in JWT
  [MaxLength(256), EmailAddress] public string Email { get; set; } = string.Empty;

  public bool EmailVerified { get; set; } = false;

  public string[] Scopes { get; set; } = [];

  // Whether it's officially marked as active
  public bool Active { get; set; } = false;
}
