using CSharp_Result;

namespace Domain.User;

public interface IUserService
{

  Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search);

  Task<Result<User?>> GetById(string id);
  Task<Result<User?>> GetByUsername(string username);

  Task<Result<UserPrincipal>> Create(string id, UserRecord record, Func<Task<Result<Unit>>>? sync);
  Task<Result<UserPrincipal?>> Update(string id, UserRecord record, Func<Task<Result<Unit>>>? sync);

  Task<Result<Unit?>> Delete(string id);
  Task<Result<Unit?>> DeleteAllRemnants(string id);

  /// <summary>
  /// Self-service account deletion of all the user's personal data (hard delete), with the
  /// donation/charge ledger anonymize-retained. When <paramref name="blockOnDebt"/> is true and
  /// the user has an outstanding debt, this fails with <c>AccountDeletionBlockedException</c>
  /// BEFORE any destructive write. Returns null when the user does not exist (already deleted),
  /// so the operation is idempotent / retry-safe. Logto identity removal is orchestrated by the
  /// caller AFTER this succeeds (DB-first ordering).
  ///
  /// <paramref name="onBeforePurge"/> runs once the debt gate has passed but BEFORE any data is
  /// deleted (e.g. revoking the Airwallex payment consent while the payment row still exists). It
  /// must be best-effort — always resolve to success — or it will block the deletion.
  /// </summary>
  Task<Result<Unit?>> DeleteAccount(string id, bool blockOnDebt, Func<Task<Result<Unit>>>? onBeforePurge = null);
}
