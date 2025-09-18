using CSharp_Result;

namespace App.StartUp.Services.Crm;

public record CrmUser
{
  public required string Email { get; init; }
  public string? FirstName { get; init; }
  public string? LastName { get; init; }
  public bool MarketingConsent { get; init; }
}

public interface ICrmManagement
{
  Task<Result<Unit>> UpsertUser(CrmUser user);
  Task<Result<Unit>> RemoveUser(string email);
}

