using App.StartUp.Database;
using CSharp_Result;
using Domain.Disbursement;
using Domain.Penalty;
using Microsoft.EntityFrameworkCore;
using NodaMoney;

namespace App.Modules.Disbursement.Data;

public class DisbursementRepository(MainDbContext db, ILogger<DisbursementRepository> logger) : IDisbursementRepository
{
  private const string PledgeSource = "pledge";

  public async Task<Result<List<ClaimedPayout>>> ClaimPendingPayouts(long minPayoutCents, int maxGroups)
  {
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();

      // Lock the charged-but-unpaid penalty rows for this pass. FOR UPDATE SKIP LOCKED holds the
      // locks until commit, so a concurrent claim cannot grab the same rows and double-donate;
      // the stamping below (DisbursementId set) is what keeps them excluded on later passes.
      // ToListAsync (not FirstOrDefault) so EF runs the raw SQL verbatim without wrapping the
      // FOR UPDATE in an unsupported subquery.
      var candidates = await db.Penalties
        .FromSqlRaw(@"
          SELECT * FROM ""Penalties""
          WHERE ""Status"" = {0} AND ""DisbursementId"" IS NULL
          ORDER BY ""CreatedAt""
          FOR UPDATE SKIP LOCKED",
          (int)PenaltyStatus.Charged)
        .ToListAsync();

      // Group by (charity, currency): one donation per pair, never summing across currencies.
      var groups = candidates
        .GroupBy(p => (p.CharityId, p.Currency))
        .Select(g => new
        {
          g.Key.CharityId,
          g.Key.Currency,
          Sum = g.Sum(x => (long)x.AmountCents),
          Ids = g.Select(x => x.Id).ToList()
        })
        .Where(g => g.Sum >= minPayoutCents)
        .OrderByDescending(g => g.Sum)
        .ToList();

      if (groups.Count == 0)
      {
        await tx.RollbackAsync();
        return new List<ClaimedPayout>();
      }

      // Resolve each charity's Pledge org id (the donation target). A charity could in principle
      // carry more than one pledge link. Pick deterministically (most recently synced, then a
      // stable tie-break on the key) — an unordered First() could send the payout to a different
      // organization across runs.
      var charityIds = groups.Select(g => g.CharityId).Distinct().ToList();
      var orgRows = await db.ExternalIds.AsNoTracking()
        .Where(e => e.Source == PledgeSource && charityIds.Contains(e.CharityId))
        .ToListAsync();
      var orgIdByCharity = orgRows
        .GroupBy(e => e.CharityId)
        .ToDictionary(
          g => g.Key,
          g => g.OrderByDescending(e => e.LastSyncedAt).ThenBy(e => e.ExternalKey, StringComparer.Ordinal).First().ExternalKey);

      var now = DateTime.UtcNow;
      var claimed = new List<ClaimedPayout>();
      foreach (var g in groups)
      {
        if (claimed.Count >= maxGroups) break;
        if (!orgIdByCharity.TryGetValue(g.CharityId, out var orgId) || string.IsNullOrWhiteSpace(orgId))
        {
          // No Pledge org id -> we cannot donate. Leave the penalties unclaimed (pending payout)
          // so they settle once the charity is (re)synced and gets an org id.
          logger.LogWarning("Skipping payout for charity {CharityId} ({Currency}): no Pledge org id", g.CharityId, g.Currency);
          continue;
        }

        var d = new DisbursementData
        {
          Id = Guid.NewGuid(), // generated up front: it is both the FK we stamp and the vendor reference
          CharityId = g.CharityId,
          Currency = g.Currency,
          AmountCents = g.Sum,
          Status = (int)DisbursementStatus.Pending,
          PledgeOrganizationId = orgId,
          Attempts = 0,
          CreatedAt = now,
          UpdatedAt = now
        };
        db.Disbursements.Add(d);
        // Insert the disbursement BEFORE stamping penalties: the penalties' DisbursementId FK
        // references this row, and ExecuteUpdate runs immediately (the row must already exist).
        await db.SaveChangesAsync();

        await db.Penalties
          .Where(p => g.Ids.Contains(p.Id))
          .ExecuteUpdateAsync(s => s
            .SetProperty(p => p.DisbursementId, d.Id)
            .SetProperty(p => p.UpdatedAt, now));

        claimed.Add(new ClaimedPayout
        {
          DisbursementId = d.Id,
          CharityId = g.CharityId,
          PledgeOrganizationId = orgId,
          Amount = new Money(g.Sum / 100m, Currency.FromCode(g.Currency))
        });
      }

      await tx.CommitAsync();
      return claimed;
    }
    catch (Exception e)
    {
      logger.LogError(e, "ClaimPendingPayouts failed for MinPayoutCents={Min} MaxGroups={Max}", minPayoutCents, maxGroups);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkDisbursed(Guid disbursementId, string providerDonationId)
  {
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();

      // Lock the disbursement row so a reconcile racing the original donate serializes here:
      // the second waits, then sees Completed and no-ops instead of overwriting the donation id.
      var d = (await db.Disbursements
          .FromSqlRaw(@"SELECT * FROM ""Disbursements"" WHERE ""Id"" = {0} FOR UPDATE", disbursementId)
          .ToListAsync())
        .FirstOrDefault();
      if (d == null)
      {
        await tx.RollbackAsync();
        return new Unit();
      }
      if (d.Status == (int)DisbursementStatus.Completed)
      {
        // Already settled; idempotent no-op.
        await tx.RollbackAsync();
        return new Unit();
      }

      d.Status = (int)DisbursementStatus.Completed;
      d.ProviderDonationId = providerDonationId;
      d.LastError = null;
      d.UpdatedAt = DateTime.UtcNow;
      await db.SaveChangesAsync();
      await tx.CommitAsync();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkDisbursed failed for Id={Id} ProviderDonationId={ProviderDonationId}", disbursementId, providerDonationId);
      throw;
    }
  }

  public async Task<Result<Unit>> MarkFailed(Guid disbursementId, string error)
  {
    try
    {
      await using var tx = await db.Database.BeginTransactionAsync();

      var d = (await db.Disbursements
          .FromSqlRaw(@"SELECT * FROM ""Disbursements"" WHERE ""Id"" = {0} FOR UPDATE", disbursementId)
          .ToListAsync())
        .FirstOrDefault();
      if (d == null)
      {
        await tx.RollbackAsync();
        return new Unit();
      }
      if (d.Status == (int)DisbursementStatus.Completed)
      {
        // A donation that already settled must never be undone/released.
        await tx.RollbackAsync();
        return new Unit();
      }

      d.Status = (int)DisbursementStatus.Failed;
      d.LastError = error;
      d.Attempts += 1;
      d.UpdatedAt = DateTime.UtcNow;

      // Release the claimed penalties so a later pass re-claims them into a fresh disbursement.
      await db.Penalties
        .Where(p => p.DisbursementId == disbursementId)
        .ExecuteUpdateAsync(s => s
          .SetProperty(p => p.DisbursementId, (Guid?)null)
          .SetProperty(p => p.UpdatedAt, DateTime.UtcNow));

      await db.SaveChangesAsync();
      await tx.CommitAsync();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "MarkFailed failed for Id={Id}", disbursementId);
      throw;
    }
  }

  public async Task<Result<List<DisbursementPrincipal>>> GetReconcilable(TimeSpan olderThan, int batchSize)
  {
    try
    {
      var cutoff = DateTime.UtcNow - olderThan;
      var rows = await db.Disbursements.AsNoTracking()
        .Where(d => d.Status == (int)DisbursementStatus.Pending && d.UpdatedAt < cutoff)
        .OrderBy(d => d.UpdatedAt)
        .Take(batchSize)
        .ToListAsync();
      return rows.Select(x => x.ToPrincipal()).ToList();
    }
    catch (Exception e)
    {
      logger.LogError(e, "GetReconcilable failed");
      throw;
    }
  }
}
