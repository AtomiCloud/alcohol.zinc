using CSharp_Result;
using Domain;
using Domain.Habit;
using Domain.User;

namespace UnitTest.User;

/// <summary>
/// Runs the supplied unit-of-work immediately, with no real transaction. Account-deletion
/// semantics (debt gate before any write, idempotency) don't depend on the TransactionScope,
/// so an in-memory pass-through is enough to exercise <see cref="UserService"/>.
/// </summary>
public sealed class ImmediateTransactionManager : ITransactionManager
{
  public Task<Result<T>> Start<T>(Func<Task<Result<T>>> func) => func();
}

/// <summary>
/// Hand-rolled fake for <see cref="IUserRepository"/> (no Moq, mirroring the repo's test style).
/// Only <see cref="DeleteAllRemnants"/> is exercised by account deletion; everything else throws.
/// </summary>
public sealed class FakeUserRepository : IUserRepository
{
  // Seedable behavior
  public bool UserExists { get; set; } = true;
  public Exception? DeleteAllRemnantsThrows { get; set; }

  // Recorded calls
  public readonly List<string> DeleteAllRemnantsCalls = [];

  public Task<Result<Unit?>> DeleteAllRemnants(string id)
  {
    this.DeleteAllRemnantsCalls.Add(id);
    if (this.DeleteAllRemnantsThrows is not null)
      return Task.FromResult<Result<Unit?>>(this.DeleteAllRemnantsThrows);
    // null mimics "user not found / already deleted" (the repo's idempotent contract).
    Unit? result = this.UserExists ? new Unit() : null;
    return Task.FromResult<Result<Unit?>>(result);
  }

  public Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search) => throw new NotImplementedException();
  public Task<Result<Domain.User.User?>> GetById(string id) => throw new NotImplementedException();
  public Task<Result<Domain.User.User?>> GetByUsername(string username) => throw new NotImplementedException();
  public Task<Result<UserPrincipal>> Create(string id, UserRecord record) => throw new NotImplementedException();
  public Task<Result<UserPrincipal?>> Update(string id, UserRecord record) => throw new NotImplementedException();
  public Task<Result<Unit?>> Delete(string id) => throw new NotImplementedException();
}

/// <summary>
/// Hand-rolled fake for <see cref="IStreakRepository"/>. Only <see cref="GetOpenDebtsForUser"/>
/// (the source of "total debt") is exercised by the account-deletion debt gate.
/// </summary>
public sealed class FakeStreakRepository : IStreakRepository
{
  public List<HabitDebtItem> OpenDebts { get; set; } = [];
  public Exception? GetOpenDebtsThrows { get; set; }
  public readonly List<string> GetOpenDebtsForUserCalls = [];

  public Task<Result<List<HabitDebtItem>>> GetOpenDebtsForUser(string userId)
  {
    this.GetOpenDebtsForUserCalls.Add(userId);
    if (this.GetOpenDebtsThrows is not null)
      return Task.FromResult<Result<List<HabitDebtItem>>>(this.GetOpenDebtsThrows);
    return Task.FromResult<Result<List<HabitDebtItem>>>(this.OpenDebts);
  }

  // Convenience: a single debt item of the given amount/currency.
  public static HabitDebtItem Debt(decimal amount, string currency = "SGD") =>
    new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new DateOnly(2026, 1, 1), amount, currency, Guid.NewGuid(), "task");

  public Task<Result<int>> GetCurrentStreak(Guid habitId, DateOnly today) => throw new NotImplementedException();
  public Task<Result<int>> GetMaxStreak(Guid habitId) => throw new NotImplementedException();
  public Task<Result<bool>> IsCompleteOn(Guid habitId, DateOnly date) => throw new NotImplementedException();
  public Task<Result<HashSet<DateOnly>>> GetCompletedInRange(Guid habitId, DateOnly start, DateOnly end) => throw new NotImplementedException();
  public Task<Result<bool>> HasCompletionBetweenUtc(Guid habitId, DateTime startUtc, DateTime endUtc) => throw new NotImplementedException();
  public Task<Result<List<DateTime>>> GetCompletionsBetweenUtc(Guid habitId, DateTime startUtc, DateTime endUtc) => throw new NotImplementedException();
  public Task<Result<List<HabitExecutionRecord>>> GetExecutionsInHabitDateRange(Guid habitId, DateOnly start, DateOnly end) => throw new NotImplementedException();
  public Task<Result<List<HabitDebtItem>>> GetOpenDebtsForHabit(Guid habitId) => throw new NotImplementedException();
  public Task<Result<int>> GetUserMaxStreakAcrossHabits(string userId) => throw new NotImplementedException();
}
