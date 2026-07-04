using App.StartUp.Options;
using App.StartUp.Services.Auth;
using CSharp_Result;
using Domain.Subscription;
using Domain.User;
using Microsoft.Extensions.Options;
using DomainUser = Domain.User.User;

namespace UnitTest.Handoff;

// Hand-rolled fakes implementing the real contracts (no Moq, matching the
// UnitTest/Subscription style) for the web-handoff service tests.

public sealed class FakeSubscriptionService(string tier) : ISubscriptionService
{
  public List<string> Calls { get; } = [];

  public Task<Result<string>> GetUserTier(string userId)
  {
    Calls.Add($"GetUserTier({userId})");
    return Task.FromResult<Result<string>>(tier);
  }

  public Task<Result<int>> GetLimitForTier(string tier1, string key) =>
    Task.FromResult<Result<int>>(int.MaxValue);
}

public sealed class FakeUserService(DomainUser? user) : IUserService
{
  public List<string> Calls { get; } = [];

  public Task<Result<DomainUser?>> GetById(string id)
  {
    Calls.Add($"GetById({id})");
    return Task.FromResult<Result<DomainUser?>>(user);
  }

  public Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search) =>
    throw new NotImplementedException();

  public Task<Result<DomainUser?>> GetByUsername(string username) => throw new NotImplementedException();

  public Task<Result<UserPrincipal>> Create(string id, UserRecord record, Func<Task<Result<Unit>>>? sync) =>
    throw new NotImplementedException();

  public Task<Result<UserPrincipal?>> Update(string id, UserRecord record, Func<Task<Result<Unit>>>? sync) =>
    throw new NotImplementedException();

  public Task<Result<Unit?>> Delete(string id) => throw new NotImplementedException();

  public Task<Result<Unit?>> DeleteAllRemnants(string id) => throw new NotImplementedException();

  public Task<Result<Unit?>> DeleteAccount(string id, bool blockOnDebt,
    Func<Task<Result<Unit>>>? onBeforePurge = null) => throw new NotImplementedException();
}

public sealed class FakeAuthManagement : IAuthManagement
{
  private readonly Result<string> _tokenResult;

  public List<(string Email, int ExpiresInSeconds)> CreateOneTimeTokenCalls { get; } = [];

  public FakeAuthManagement(string token) => _tokenResult = token;

  public FakeAuthManagement(Exception failure) => _tokenResult = failure;

  public Task<Result<string>> CreateOneTimeToken(string email, int expiresInSeconds)
  {
    CreateOneTimeTokenCalls.Add((email, expiresInSeconds));
    return Task.FromResult(_tokenResult);
  }

  public Task<Result<Unit>> AssignRole(string userId, string roleId) => throw new NotImplementedException();

  public Task<Result<Unit>> RemoveRole(string userId, string roleId) => throw new NotImplementedException();

  public Task<Result<Unit>> SetClaim(string userId, string claimKey, string claimValue) =>
    throw new NotImplementedException();

  public Task<Result<Unit>> RemoveClaim(string userId, string claimKey) => throw new NotImplementedException();

  public Task<Result<Unit>> DeleteUser(string userId) => throw new NotImplementedException();
}

public sealed class FakeOptionsMonitor(WebPortalOption value) : IOptionsMonitor<WebPortalOption>
{
  public WebPortalOption CurrentValue => value;

  public WebPortalOption Get(string? name) => value;

  public IDisposable? OnChange(Action<WebPortalOption, string?> listener) => null;
}

public static class Users
{
  public static DomainUser Make(string id, string email) => new()
  {
    Principal = new UserPrincipal
    {
      Id = id,
      Record = new UserRecord
      {
        Username = "testuser",
        Email = email,
        EmailVerified = true,
        Active = true,
        Scopes = [],
      },
    },
  };
}
