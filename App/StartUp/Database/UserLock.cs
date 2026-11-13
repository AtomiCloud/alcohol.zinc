using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace App.StartUp.Database;

// One Postgres advisory lock per user, held until the current transaction ends (never a
// session lock, which would outlive the transaction on a pooled connection). A transaction that
// counts one of the user's limits and then writes (habits, skips, vacation windows) takes it, and
// so does every check settle. Each takes it first, before any row lock, so they all lock in one
// order. Callers must not call payment, Logto or notification services while they hold it.
public static class UserLock
{
  public static async Task LockUser(this DatabaseFacade db, string userId)
  {
    // Applies to every lock wait for the rest of the transaction (IsLockTimeout).
    await db.ExecuteSqlRawAsync("SET LOCAL lock_timeout = '5s'");
    await db.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock(hashtextextended({"user:" + userId}, 0))");
  }

  // A lock wait ran out: another transaction of the user held the lock (or the row) too long.
  public static bool IsLockTimeout(Exception e) =>
    (e as PostgresException ?? e.InnerException as PostgresException)?.SqlState == PostgresErrorCodes.LockNotAvailable;
}
