using System.Transactions;
using App.Error.V1;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.User;
using EntityFramework.Exceptions.Common;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Users.Data;

public class UserRepository(MainDbContext db, ILogger<UserRepository> logger) : IUserRepository
{
  public async Task<Result<IEnumerable<UserPrincipal>>> Search(UserSearch search)
  {
    try
    {
      logger.LogInformation("Searching for User with '{@Search}'", search);

      var query = db.Users.AsQueryable();
      if (!string.IsNullOrWhiteSpace(search.Username))
        query = query.Where(x => EF.Functions.ILike(x.Username, $"%{search.Username}%"));
      if (!string.IsNullOrWhiteSpace(search.Id))
        query = query.Where(x => EF.Functions.ILike(x.Id.ToString(), $"%{search.Id}%"));
      if (!string.IsNullOrEmpty(search.Email))
        query = query.Where(x => EF.Functions.ILike(x.Email, $"%{search.Email}%"));
      if (search.EmailVerified != null)
        query = query.Where(x => x.EmailVerified == search.EmailVerified);
      if (search.Active != null)
        query = query.Where(x => x.Active == search.Active);

      var result = await query
        .Skip(search.Skip)
        .Take(search.Limit)
        .ToArrayAsync();

      return result
        .Select(x => x.ToPrincipal())
        .ToResult();
    }
    catch (Exception e)
    {
      logger
        .LogError(e, "Failed search for User with {@Search}", search);
      return e;
    }
  }

  public async Task<Result<User?>> GetById(string id)
  {
    try
    {
      logger.LogInformation("Retrieving User with Id '{Id}'", id);
      var user = await db
        .Users
        .Where(x => x.Id == id)
        .FirstOrDefaultAsync();
      return user?.ToDomain();
    }
    catch (Exception e)
    {
      logger
        .LogError(e, "Failed retrieving User with Id: {Id}", id);
      throw;
    }
  }

  public async Task<Result<User?>> GetByUsername(string username)
  {
    try
    {
      logger.LogInformation("Retrieving User by Username: {Username}", username);
      var user = await db.Users
        .Where(x => x.Username == username)
        .FirstOrDefaultAsync();
      return user?.ToDomain();
    }
    catch (Exception e)
    {
      logger
        .LogError(e, "Failed retrieving User by Username: {Username}", username);
      throw;
    }
  }

  public async Task<Result<UserPrincipal>> Create(string id, UserRecord record)
  {
    try
    {
      // Creating user
      logger.LogInformation("Creating User: {@Record}", record.ToJson());
      var data = record.ToData();
      data.Id = id;
      var r = db.Users.Add(data);
      await db.SaveChangesAsync();
      return r.Entity.ToPrincipal();
    }
    catch (UniqueConstraintException e)
    {
      logger.LogError(e,
        "Failed to create User due to conflicting with existing record for JWT sub '{Sub}': {@Record}", id,
        record.ToJson());

      return new EntityConflict("Failed to create User due to conflicting with existing record", typeof(UserPrincipal))
        .ToException();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to create User for JWT sub '{Sub}': {@Record}", id, record.ToJson());
      throw;
    }
  }

  public async Task<Result<UserPrincipal?>> Update(string id, UserRecord v2)
  {
    try
    {
      logger.LogInformation("Updating User '{Id}' with: {@Record}", id, v2.ToJson());
      var v1 = await db.Users
        .Where(x => x.Id == id)
        .FirstOrDefaultAsync();
      if (v1 == null) return (UserPrincipal?)null;

      var v3 = v1.Update(v2);

      var updated = db.Users.Update(v3);
      await db.SaveChangesAsync();
      return updated.Entity.ToPrincipal();
    }
    catch (UniqueConstraintException e)
    {
      logger.LogError(e,
        "Failed to update User due to conflicting with existing record for JWT sub '{Sub}': {@Record}", id,
        v2.ToJson());
      return new EntityConflict("Failed to update User due to conflicting with existing record", typeof(UserPrincipal))
        .ToException();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to update User for JWT sub '{Sub}': {@Record}", id, v2.ToJson());
      throw;
    }
  }

  public async Task<Result<Unit?>> Delete(string id)
  {
    try
    {
      logger.LogInformation("Deleting User '{Id}'", id);
      var a = await db.Users
        .Where(x => x.Id == id)
        .FirstOrDefaultAsync();
      if (a == null) return (Unit?)null;

      db.Users.Remove(a);
      await db.SaveChangesAsync();
      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to delete User record with ID '{Id}", id);
      throw;
    }
  }

  public async Task<Result<Unit?>> DeleteAllRemnants(string id)
  {
    try
    {
      logger.LogInformation("Deleting all remnants for User '{Id}'", id);

      // Check if user exists
      var user = await db.Users
        .Where(x => x.Id == id)
        .FirstOrDefaultAsync();
      if (user == null) return (Unit?)null;

      // Get all habits for this user
      var userHabits = await db.Habits
        .Where(h => h.UserId == id)
        .ToListAsync();

      var habitIds = userHabits.Select(h => h.Id).ToList();

      if (habitIds.Any())
      {
        // Delete freeze awards (the freeze-grant ledger is keyed by HabitId, not UserId)
        var freezeAwards = await db.FreezeAwards
          .Where(fa => habitIds.Contains(fa.HabitId))
          .ToListAsync();
        if (freezeAwards.Any())
        {
          logger.LogInformation("Deleting {Count} freeze awards for User '{Id}'", freezeAwards.Count, id);
          db.FreezeAwards.RemoveRange(freezeAwards);
        }

        // Get all habit versions for these habits
        var habitVersionIds = await db.HabitVersions
          .Where(hv => habitIds.Contains(hv.HabitId))
          .Select(hv => hv.Id)
          .ToListAsync();

        if (habitVersionIds.Any())
        {
          // Delete all habit executions for these habit versions
          var habitExecutions = await db.HabitExecutions
            .Where(he => habitVersionIds.Contains(he.HabitVersionId))
            .ToListAsync();

          if (habitExecutions.Any())
          {
            logger.LogInformation("Deleting {Count} habit executions for User '{Id}'", habitExecutions.Count, id);
            db.HabitExecutions.RemoveRange(habitExecutions);
          }

          // Delete all habit versions
          var habitVersions = await db.HabitVersions
            .Where(hv => habitIds.Contains(hv.HabitId))
            .ToListAsync();

          logger.LogInformation("Deleting {Count} habit versions for User '{Id}'", habitVersions.Count, id);
          db.HabitVersions.RemoveRange(habitVersions);
        }

        // Delete all habits
        logger.LogInformation("Deleting {Count} habits for User '{Id}'", userHabits.Count, id);
        db.Habits.RemoveRange(userHabits);
      }

      // Delete vacation periods
      var vacationPeriods = await db.VacationPeriods
        .Where(v => v.UserId == id)
        .ToListAsync();
      if (vacationPeriods.Any())
      {
        logger.LogInformation("Deleting {Count} vacation periods for User '{Id}'", vacationPeriods.Count, id);
        db.VacationPeriods.RemoveRange(vacationPeriods);
      }

      // Delete protection freeze balance
      var userProtections = await db.UserProtections
        .Where(p => p.UserId == id)
        .ToListAsync();
      if (userProtections.Any())
      {
        logger.LogInformation("Deleting {Count} protection records for User '{Id}'", userProtections.Count, id);
        db.UserProtections.RemoveRange(userProtections);
      }

      // Delete freeze consumptions (the freeze-spend ledger, keyed by UserId)
      var freezeConsumptions = await db.FreezeConsumptions
        .Where(fc => fc.UserId == id)
        .ToListAsync();
      if (freezeConsumptions.Any())
      {
        logger.LogInformation("Deleting {Count} freeze consumptions for User '{Id}'", freezeConsumptions.Count, id);
        db.FreezeConsumptions.RemoveRange(freezeConsumptions);
      }

      // Anonymize-retain the donation/charge ledger (penalty charges + charity donations).
      AnonymizePenaltyLedger(id);

      // Delete configuration if exists
      var configuration = await db.Configurations
        .Where(c => c.UserId == id)
        .FirstOrDefaultAsync();

      if (configuration != null)
      {
        logger.LogInformation("Deleting configuration for User '{Id}'", id);
        db.Configurations.Remove(configuration);
      }

      // Delete payment customer if exists
      var paymentCustomer = await db.PaymentCustomers
        .Where(pc => pc.UserId == id)
        .FirstOrDefaultAsync();

      if (paymentCustomer != null)
      {
        logger.LogInformation("Deleting payment customer for User '{Id}'", id);
        db.PaymentCustomers.Remove(paymentCustomer);
      }

      // Finally, delete the user
      logger.LogInformation("Deleting User '{Id}'", id);
      db.Users.Remove(user);

      // Save all changes
      await db.SaveChangesAsync();
      logger.LogInformation("Successfully deleted all remnants for User '{Id}'", id);

      return new Unit();
    }
    catch (Exception e)
    {
      logger.LogError(e, "Failed to delete all remnants for User with ID '{Id}'", id);
      throw;
    }
  }

  // ── Penalty anonymize-retain seam ─────────────────────────────────────────────────────────────
  // The Penalty module (PenaltyData / CharityBalanceData) does NOT exist on `main` yet — it lives on
  // the unmerged `Yek-Khan/habit-penalty-feature` branch — so there are no penalty rows to act on and
  // this is intentionally a no-op for now (safe: zero penalty PII exists on this branch).
  //
  // TODO(account-deletion): when the Penalty module merges to main, ANONYMIZE-RETAIN here instead of
  // hard-deleting. For every PenaltyData row where UserId == id:
  //   - set UserId = a sentinel (e.g. "deleted-user") to DETACH the person from the financial record;
  //   - KEEP AmountCents, Currency, CharityId, CreatedAt and PaymentIntentId (accounting/legal record);
  //   - do NOT delete the row; CharityBalanceData holds no user PII and stays untouched.
  // It must mutate tracked entities so the single SaveChangesAsync below (same transaction) persists it.
  // A skipped unit test already asserts the expected behavior — un-skip it once the module exists.
  private void AnonymizePenaltyLedger(string id)
  {
    logger.LogDebug(
      "Penalty anonymize-retain seam for User '{Id}': no-op until the Penalty module merges to main", id);
  }
}
