using CSharp_Result;
using Domain.Exceptions;
using Domain.Habit;

namespace Domain.User;

public class UserService(
  IUserRepository repo,
  IStreakRepository streakRepo,
  ITransactionManager tm
) : IUserService
{
  public Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search)
  {
    return repo.Search(search);
  }

  public Task<Result<User?>> GetById(string id)
  {
    return repo.GetById(id);
  }

  public Task<Result<User?>> GetByUsername(string username)
  {
    return repo.GetByUsername(username);
  }

  public Task<Result<UserPrincipal>> Create(string id, UserRecord record, Func<Task<Result<Unit>>>? sync)
  {
    return tm.Start(() =>
      repo.Create(id, record)
        .DoAwait(DoType.MapErrors, _ => sync?.Invoke() ?? new Unit().ToAsyncResult())
    );
  }

  public Task<Result<UserPrincipal?>> Update(string id, UserRecord record, Func<Task<Result<Unit>>>? sync)
  {
    return tm.Start(() => repo.Update(id, record)
      .DoAwait(DoType.MapErrors, _ => sync?.Invoke() ?? new Unit().ToAsyncResult())
    );
  }

  public Task<Result<Unit?>> Delete(string id)
  {
    return repo.Delete(id);
  }

  public Task<Result<Unit?>> DeleteAllRemnants(string id)
  {
    return tm.Start(() => repo.DeleteAllRemnants(id));
  }

  public Task<Result<Unit?>> DeleteAccount(string id, bool blockOnDebt, Func<Task<Result<Unit>>>? onBeforePurge = null)
  {
    // Ordering: debt gate -> onBeforePurge -> purge. The gate (a read) and onBeforePurge run BEFORE
    // and OUTSIDE the DB transaction; only the purge is wrapped in tm.Start. onBeforePurge is
    // best-effort external provider cleanup (e.g. an Airwallex consent revoke that does an HTTP call
    // AND its own DB write) — running it inside the purge's TransactionScope would hold DB locks
    // across the HTTP round-trip and could force a multi-connection distributed-transaction promotion
    // (which Npgsql does not support). The gate still runs before any destructive action, so a
    // blocked deletion never touches the payment method. DeleteAllRemnants is idempotent (null when
    // the user is already gone), keeping a retry after a partial failure safe.
    return GuardNoDebt(id, blockOnDebt)
      .ThenAwait(_ => onBeforePurge?.Invoke() ?? new Unit().ToAsyncResult())
      .ThenAwait(_ => tm.Start(() => repo.DeleteAllRemnants(id)));
  }

  // Returns success when the gate is off or the user owes nothing; otherwise fails with
  // AccountDeletionBlockedException carrying the outstanding amount (mapped to 409 by the App layer).
  private Task<Result<Unit>> GuardNoDebt(string id, bool blockOnDebt)
  {
    if (!blockOnDebt) return new Unit().ToAsyncResult();
    return streakRepo.GetOpenDebtsForUser(id)
      .Then(debts => (Total: debts.Sum(d => d.Amount),
        Currency: debts.FirstOrDefault()?.Currency ?? string.Empty), Errors.MapAll)
      .Then<(decimal Total, string Currency), Unit>(d => d.Total > 0
        ? new AccountDeletionBlockedException(d.Total, d.Currency)
        : new Unit());
  }
}
