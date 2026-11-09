using App.Modules.HabitExecution.Data;
using App.StartUp.Database;
using CSharp_Result;
using Domain.Check;
using Domain.Habit;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace App.Modules.Check.Data;

public class CheckRepository(MainDbContext db, ILogger<CheckRepository> logger) : ICheckRepository
{
  // A claimed outbox event whose worker crashed becomes claimable again after this.
  private static readonly TimeSpan ClaimLease = TimeSpan.FromMinutes(10);

  public async Task<Result<List<SchedulableHabit>>> ListSchedulableHabits(string? userId, List<Guid>? habitIds)
  {
    try
    {
      var query = from h in db.Habits
                  join hv in db.HabitVersions on new { h.Id, h.Version } equals new { Id = hv.HabitId, hv.Version }
                  join c in db.Configurations on h.UserId equals c.UserId into cs
                  from c in cs.DefaultIfEmpty()
                  where h.Enabled && h.DeletedAt == null
                  select new
                  {
                    h.Id,
                    h.UserId,
                    VersionId = hv.Id,
                    hv.DaysOfWeek,
                    hv.DeadlineTime,
                    HabitTimezone = hv.Timezone,
                    UserTimezone = c == null ? null : c.Timezone
                  };
      if (userId != null) query = query.Where(x => x.UserId == userId);
      if (habitIds != null) query = query.Where(x => habitIds.Contains(x.Id));

      var rows = await query.AsNoTracking().ToListAsync();
      // The user's timezone (Configuration) wins; fall back to the habit's when it's unknown.
      return rows.Select(x => new SchedulableHabit(
          x.Id, x.UserId, x.VersionId, x.DaysOfWeek, x.DeadlineTime,
          x.UserTimezone != null && TimeZoneInfo.TryFindSystemTimeZoneById(x.UserTimezone, out _)
            ? x.UserTimezone
            : x.HabitTimezone))
        .ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListSchedulableHabits failed for UserId={UserId}", userId);
      throw;
    }
  }

  public async Task<Result<int>> CreatePending(List<NewCheck> checks)
  {
    try
    {
      if (checks.Count == 0) return 0;
      var habitIds = checks.Select(c => c.HabitId).ToArray();
      var dates = checks.Select(c => c.LocalDate).ToArray();
      var tzs = checks.Select(c => c.Timezone).ToArray();
      var opens = checks.Select(c => DateTime.SpecifyKind(c.OpensAtUtc, DateTimeKind.Utc)).ToArray();
      var deadlines = checks.Select(c => DateTime.SpecifyKind(c.DeadlineUtc, DateTimeKind.Utc)).ToArray();
      return await db.Database.ExecuteSqlAsync($@"
        INSERT INTO ""HabitChecks"" (""Id"", ""HabitId"", ""LocalDate"", ""Timezone"", ""OpensAtUtc"", ""DeadlineUtc"", ""Status"")
        SELECT gen_random_uuid(), x.h, x.d, x.tz, x.o, x.dl, {(int)HabitCheckStatusData.Pending}
        FROM unnest({habitIds}::uuid[], {dates}::date[], {tzs}::text[], {opens}::timestamptz[], {deadlines}::timestamptz[])
          AS x(h, d, tz, o, dl)
        ON CONFLICT (""HabitId"", ""LocalDate"") DO NOTHING");
    }
    catch (Exception e)
    {
      logger.LogError(e, "CreatePending failed for {Count} checks", checks.Count);
      throw;
    }
  }

  public async Task<Result<List<CheckPrincipal>>> ListDuePending(DateTime deadlineBeforeUtc, int limit)
  {
    try
    {
      var data = await db.HabitChecks.AsNoTracking()
        .Where(c => c.Status == HabitCheckStatusData.Pending && c.DeadlineUtc <= deadlineBeforeUtc)
        .OrderBy(c => c.DeadlineUtc)
        .Take(limit)
        .ToListAsync();
      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListDuePending failed");
      throw;
    }
  }

  public async Task<Result<List<CheckPrincipal>>> ListPending(List<Guid> habitIds)
  {
    try
    {
      var data = await db.HabitChecks.AsNoTracking()
        .Where(c => c.Status == HabitCheckStatusData.Pending && habitIds.Contains(c.HabitId))
        .ToListAsync();
      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListPending failed");
      throw;
    }
  }

  public async Task<Result<List<CheckPrincipal>>> ListCovering(Guid habitId, DateTime atUtc)
  {
    try
    {
      var at = DateTime.SpecifyKind(atUtc, DateTimeKind.Utc);
      var data = await db.HabitChecks.AsNoTracking()
        .Where(c => c.HabitId == habitId && c.OpensAtUtc <= at && c.DeadlineUtc > at)
        .ToListAsync();
      return data.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListCovering failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<CheckPrincipal?>> Get(Guid habitId, DateOnly localDate)
  {
    try
    {
      var data = await db.HabitChecks.AsNoTracking()
        .FirstOrDefaultAsync(c => c.HabitId == habitId && c.LocalDate == localDate);
      return data?.ToPrincipal();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Get check failed for HabitId={HabitId} Date={Date}", habitId, localDate);
      throw;
    }
  }

  public async Task<Result<bool>> UpdatePendingWindow(Guid id, string timezone, DateTime opensAtUtc, DateTime deadlineUtc)
  {
    try
    {
      var opens = DateTime.SpecifyKind(opensAtUtc, DateTimeKind.Utc);
      var deadline = DateTime.SpecifyKind(deadlineUtc, DateTimeKind.Utc);
      var n = await db.HabitChecks
        .Where(c => c.Id == id && c.Status == HabitCheckStatusData.Pending)
        .ExecuteUpdateAsync(s => s
          .SetProperty(c => c.Timezone, timezone)
          .SetProperty(c => c.OpensAtUtc, opens)
          .SetProperty(c => c.DeadlineUtc, deadline));
      return n > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "UpdatePendingWindow failed for CheckId={CheckId}", id);
      throw;
    }
  }

  public async Task<Result<int>> DeletePending(List<Guid> ids)
  {
    try
    {
      return await db.HabitChecks
        .Where(c => ids.Contains(c.Id) && c.Status == HabitCheckStatusData.Pending)
        .ExecuteDeleteAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "DeletePending failed");
      throw;
    }
  }

  public async Task<Result<HashSet<Guid>>> ListRecorded(List<Guid> checkIds)
  {
    try
    {
      var ids = await (from c in db.HabitChecks
                       where checkIds.Contains(c.Id)
                             && (from he in db.HabitExecutions
                                 join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                                 where hv.HabitId == c.HabitId && he.Date == c.LocalDate
                                 select he.Id).Any()
                       select c.Id).ToListAsync();
      return ids.ToHashSet();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ListRecorded failed");
      throw;
    }
  }

  public async Task<Result<bool>> EvidenceExists(Guid habitId, string proofKey, string externalId)
  {
    try
    {
      return await db.HabitEvidence.AsNoTracking()
        .AnyAsync(x => x.HabitId == habitId && x.ProofKey == proofKey && x.ExternalId == externalId);
    }
    catch (Exception e)
    {
      logger.LogError(e, "EvidenceExists failed for HabitId={HabitId}", habitId);
      throw;
    }
  }

  public async Task<Result<SettleOutcome?>> Settle(SettleCheck cmd)
  {
    await using var tx = await db.Database.BeginTransactionAsync();
    try
    {
      // Lock the check so concurrent settles serialize; only a Pending check settles.
      var check = (await db.HabitChecks
          .FromSqlRaw(@"SELECT * FROM ""HabitChecks"" WHERE ""Id"" = {0} FOR UPDATE", cmd.CheckId)
          .ToListAsync())
        .FirstOrDefault();
      if (check == null || check.Status != HabitCheckStatusData.Pending)
        return await Abort();

      if (cmd.Evidence != null && await db.HabitEvidence.AnyAsync(x =>
            x.HabitId == check.HabitId && x.ProofKey == cmd.Evidence.ProofKey &&
            x.ExternalId == cmd.Evidence.ExternalId))
        return await Abort();

      // A day another writer already recorded (an older release, or a legacy execution)
      // stays as it is: the check adopts that execution instead of adding a second one.
      var executionId = Guid.NewGuid();
      var settledAt = DateTime.SpecifyKind(cmd.SettledAtUtc, DateTimeKind.Utc);
      DateTime? completedAt = cmd.CompletedAtUtc == null
        ? null
        : DateTime.SpecifyKind(cmd.CompletedAtUtc.Value, DateTimeKind.Utc);
      var inserted = await db.Database.ExecuteSqlAsync($@"
        INSERT INTO ""HabitExecutions"" (""Id"", ""HabitVersionId"", ""Date"", ""Status"", ""CompletedAt"", ""Notes"", ""PaymentProcessed"")
        SELECT {executionId}, {cmd.HabitVersionId}, {check.LocalDate}, {(int)cmd.Status.ToExecutionStatus()}, {completedAt}, {cmd.Notes}, false
        WHERE NOT EXISTS (
          SELECT 1 FROM ""HabitExecutions"" he
          JOIN ""HabitVersions"" settled ON settled.""Id"" = he.""HabitVersionId""
          WHERE settled.""HabitId"" = {check.HabitId} AND he.""Date"" = {check.LocalDate}
        )");
      var adopted = inserted == 0;

      var execution = adopted
        ? await (from he in db.HabitExecutions
                 join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                 where hv.HabitId == check.HabitId && he.Date == check.LocalDate
                 orderby he.Id
                 select he).AsNoTracking().FirstAsync()
        : await db.HabitExecutions.AsNoTracking().FirstAsync(x => x.Id == executionId);
      var status = adopted ? execution.Status.ToCheckStatus().ToData() : cmd.Status.ToData();

      check.Status = status;
      check.SettledAtUtc = settledAt;
      check.SettledBy = adopted ? "adopted" : cmd.SettledBy;
      check.ExecutionId = execution.Id;

      if (cmd.Evidence != null && !adopted)
      {
        db.HabitEvidence.Add(new HabitEvidenceData
        {
          HabitId = check.HabitId,
          CheckId = check.Id,
          ProofKey = cmd.Evidence.ProofKey,
          ExternalId = cmd.Evidence.ExternalId,
          Passed = cmd.Evidence.Verdict == EvidenceVerdict.Passed,
          OccurredAtUtc = DateTime.SpecifyKind(cmd.Evidence.OccurredAtUtc, DateTimeKind.Utc),
          ReceivedAtUtc = settledAt,
          Data = cmd.Evidence.Data
        });
      }

      db.CheckEvents.Add(new CheckEventData
      {
        CheckId = check.Id,
        HabitId = check.HabitId,
        UserId = cmd.UserId,
        Type = "CheckSettled",
        Outcome = status,
        ExecutionId = execution.Id,
        CreatedAtUtc = settledAt,
        NextAttemptAtUtc = settledAt
      });

      await db.SaveChangesAsync();
      await tx.CommitAsync();
      db.ChangeTracker.Clear();
      return new SettleOutcome(execution.ToPrincipal(), adopted);
    }
    catch (Exception e) when (e is UniqueConstraintException or PostgresException { SqlState: "23505" })
    {
      // Lost a race to another writer for the same evidence or day: nothing changed.
      return await Abort();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Settle failed for CheckId={CheckId}", cmd.CheckId);
      await tx.RollbackAsync();
      db.ChangeTracker.Clear();
      throw;
    }

    async Task<Result<SettleOutcome?>> Abort()
    {
      await tx.RollbackAsync();
      // Drop tracked changes so a later SaveChanges on this scoped context doesn't replay them.
      db.ChangeTracker.Clear();
      return (SettleOutcome?)null;
    }
  }

  public async Task<Result<List<CheckEventPrincipal>>> ClaimEvents(int batchSize, int maxAttempts, DateTime nowUtc)
  {
    try
    {
      var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
      // Claim atomically: FOR UPDATE SKIP LOCKED hands each event to one worker, and
      // pushing NextAttemptAtUtc out by the lease hides it from other workers meanwhile.
      var claimed = await db.CheckEvents
        .FromSqlRaw(@"
          UPDATE ""CheckEvents"" SET ""Attempts"" = ""Attempts"" + 1, ""NextAttemptAtUtc"" = {0}
          WHERE ""Id"" IN (
            SELECT ""Id"" FROM ""CheckEvents""
            WHERE ""DispatchedAtUtc"" IS NULL AND ""NextAttemptAtUtc"" <= {1} AND ""Attempts"" < {2}
            ORDER BY ""CreatedAtUtc""
            LIMIT {3}
            FOR UPDATE SKIP LOCKED
          )
          RETURNING *", now + ClaimLease, now, maxAttempts, batchSize)
        .AsNoTracking()
        .ToListAsync();
      return claimed.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "ClaimEvents failed");
      throw;
    }
  }

  public async Task<Result<bool>> HasDelivery(Guid eventId, string bindingId)
  {
    try
    {
      return await db.CheckEventDeliveries.AsNoTracking()
        .AnyAsync(x => x.EventId == eventId && x.BindingId == bindingId);
    }
    catch (Exception e)
    {
      logger.LogError(e, "HasDelivery failed for EventId={EventId}", eventId);
      throw;
    }
  }

  public async Task<Result<bool>> RecordDelivery(Guid eventId, string bindingId)
  {
    try
    {
      var n = await db.Database.ExecuteSqlAsync($@"
        INSERT INTO ""CheckEventDeliveries"" (""Id"", ""EventId"", ""BindingId"", ""DeliveredAtUtc"")
        VALUES (gen_random_uuid(), {eventId}, {bindingId}, now())
        ON CONFLICT (""EventId"", ""BindingId"") DO NOTHING");
      return n > 0;
    }
    catch (Exception e)
    {
      logger.LogError(e, "RecordDelivery failed for EventId={EventId}", eventId);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkDispatched(Guid eventId, DateTime nowUtc)
  {
    try
    {
      var now = DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc);
      await db.CheckEvents.Where(x => x.Id == eventId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.DispatchedAtUtc, now).SetProperty(x => x.LastError, (string?)null));
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkDispatched failed for EventId={EventId}", eventId);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkFailed(Guid eventId, string error, DateTime nextAttemptAtUtc)
  {
    try
    {
      var next = DateTime.SpecifyKind(nextAttemptAtUtc, DateTimeKind.Utc);
      var message = error.Length > 1024 ? error[..1024] : error;
      await db.CheckEvents.Where(x => x.Id == eventId)
        .ExecuteUpdateAsync(s => s.SetProperty(x => x.NextAttemptAtUtc, next).SetProperty(x => x.LastError, message));
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkFailed failed for EventId={EventId}", eventId);
      throw;
    }
  }

  public async Task<Result<FailedExecutionRow?>> GetStakeForExecution(Guid executionId)
  {
    try
    {
      return await (from he in db.HabitExecutions
                    join hv in db.HabitVersions on he.HabitVersionId equals hv.Id
                    join h in db.Habits on hv.HabitId equals h.Id
                    where he.Id == executionId && he.Status == HabitExecutionStatusData.Failed
                    select new FailedExecutionRow(he.Id, h.UserId, hv.CharityId, hv.StakeCents, hv.StakeCurrency,
                      hv.RatioBasisPoints))
        .AsNoTracking()
        .FirstOrDefaultAsync();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetStakeForExecution failed for ExecutionId={ExecutionId}", executionId);
      throw;
    }
  }
}
