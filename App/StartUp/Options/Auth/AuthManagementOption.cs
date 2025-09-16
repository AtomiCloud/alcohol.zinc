using System.ComponentModel.DataAnnotations;

namespace App.StartUp.Options.Auth;

public class AuthManagementOption
{
  [Url, MinLength(1)] public string Endpoint { get; set; } = string.Empty;

  [MinLength(1)] public string Id { get; set; } = string.Empty;
  [MinLength(1)] public string Secret { get; set; } = string.Empty;

  [Url, MinLength(1)] public string Resource { get; set; } = string.Empty;

  public string Scope { get; set; } = string.Empty;
}
