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
  /// </summary>
  Task<Result<Unit?>> DeleteAccount(string id, bool blockOnDebt);
}
