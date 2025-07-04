using System.ComponentModel.DataAnnotations;

namespace App.Modules.Users.Data;

public class UserData
{
  // JWT Sub
  [MaxLength(128)]
  public string Id { get; set; } = string.Empty;

  // Custom Username
  [MaxLength(256)]
  public string Username { get; set; } = string.Empty;
}
