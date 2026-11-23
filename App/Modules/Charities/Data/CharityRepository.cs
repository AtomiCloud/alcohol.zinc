using System.Transactions;
using App.Error.V1;
using App.Modules.Notification.Data;
using App.Modules.Penalty.Data;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Charity;
using Domain.Penalty;
using Microsoft.EntityFrameworkCore;

namespace App.Modules.Charities.Data
{
    public class CharityRepository(MainDbContext db, ILogger<CharityRepository> logger) : ICharityRepository
    {
        public async Task<Result<Charity?>> Get(Guid id)
        {
            try
            {
                logger.LogInformation("Retrieving Charity by Id: {Id}", id);

                var data = await db
                    .Charities
                    .Where(x => x.Id == id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Charity not found for Id: {Id}", id);

                    return (Charity?)null;
                }
                return data.ToDomain();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed retrieving Charity by Id: {Id}", id);
                throw;
            }
        }

        public async Task<Result<IEnumerable<CharityPrincipal>>> Search(CharitySearch search)
        {
            try
            {
                logger.LogInformation("Searching Charities with '{@Search}'", search);

                // Switched-off (deleted) charities are hidden from search; Get(id) still returns them.
                IQueryable<CharityData> query = db.Charities.Where(x => x.DeletedAt == null);

                if (!string.IsNullOrWhiteSpace(search.Name))
                    query = query.Where(x => EF.Functions.ILike(x.Name, $"%{search.Name}%"));

                if (!string.IsNullOrWhiteSpace(search.Slug))
                    query = query.Where(x => x.Slug != null && EF.Functions.ILike(x.Slug!, $"%{search.Slug}%"));

                if (!string.IsNullOrWhiteSpace(search.PrimaryRegistrationNumber))
                    query = query.Where(x => x.PrimaryRegistrationNumber == search.PrimaryRegistrationNumber);

                if (!string.IsNullOrWhiteSpace(search.PrimaryRegistrationCountry))
                    query = query.Where(x => x.PrimaryRegistrationCountry == search.PrimaryRegistrationCountry);

                if (!string.IsNullOrWhiteSpace(search.Country))
                    query = query.Where(x => x.Countries != null && x.Countries.Contains(search.Country));

                if (!string.IsNullOrWhiteSpace(search.CauseKey))
                {
                    query = query
                        .Join(db.CharityCauses, c => c.Id, cc => cc.CharityId, (c, cc) => new { c, cc })
                        .Join(db.Causes, x => x.cc.CauseId, cause => cause.Id, (x, cause) => new { x.c, cause })
                        .Where(y => y.cause.Key == search.CauseKey)
                        .Select(y => y.c)
                        .Distinct();
                }

                var data = await query
                    .Skip(search.Skip)
                    .Take(search.Limit)
                    .ToListAsync();
                return data.Select(x => x.ToPrincipal()).ToList();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed searching Charities with '{@Search}'", search);
                throw;
            }
        }

        public async Task<Result<CharityPrincipal>> Create(CharityRecord model)
        {
            try
            {
                logger.LogInformation("Adding Charity: {Name}", model.Name);

                var data = model.ToData();
                var r = db.Charities.Add(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity added with Id: {Id}", data.Id);

                return r.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to create Charity: {Name}", model.Name);
                throw;
            }
        }

        public async Task<Result<CharityPrincipal?>> Update(Guid id, CharityRecord record)
        {
            try
            {
                logger.LogInformation("Updating Charity Id: {Id}", id);

                var data = await db
                    .Charities
                    .Where(x => x.Id == id)
                    .FirstOrDefaultAsync();
                if (data == null)
                {
                    logger.LogWarning("Charity not found for update, Id: {Id}", id);

                    return (CharityPrincipal?)null;
                }
                data = data.ToData(record);
                var updated = db.Charities.Update(data);
                // Metadata edits never touch the switched-off flag: a stale save must not undo a Delete.
                updated.Property(x => x.DeletedAt).IsModified = false;
                updated.Property(x => x.DecommissionedAt).IsModified = false;
                await db.SaveChangesAsync();

                logger.LogInformation("Charity updated for Id: {Id}", id);

                return updated.Entity.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to update Charity Id: {Id}", id);
                throw;
            }
        }

        // Deleting a charity SWITCHES IT OFF; it never erases the row. Penalties, accrued balances,
        // payouts and habit history point at it (the database refuses a hard delete of a charity that
        // has them). Switched off = hidden from search and refused as a new choice for a habit or a
        // default; Get(id) still returns it. Users' default-charity settings that point at it are
        // cleared (the rest of the settings row is kept). Deleting an already switched-off charity is
        // an idempotent success.
        public async Task<Result<Unit?>> Delete(Guid id)
        {
            // Own transaction only when the caller has none (an ambient TransactionScope cannot nest one).
            // Disposing it uncommitted rolls everything back.
            await using var ownTx = Transaction.Current == null && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync()
                : null;
            try
            {
                logger.LogInformation("Switching off (soft-deleting) Charity Id: {Id}", id);

                // Lock the charity row FOR UPDATE. Choosing a charity (habit create/edit, default charity)
                // reads it FOR SHARE in the same transaction as its write, so the two serialize: either the
                // choice commits first and its default is cleared below, or this commits first and the
                // choice sees the charity switched off and is refused.
                var locked = await db.Database
                    .SqlQuery<Guid>($@"SELECT ""Id"" AS ""Value"" FROM ""Charities"" WHERE ""Id"" = {id} FOR UPDATE")
                    .ToListAsync();
                if (locked.Count == 0)
                {
                    logger.LogWarning("Charity not found for delete, Id: {Id}", id);
                    return (Unit?)null;
                }

                // Written straight to the database (not through a tracked entity, which could hold a value
                // from an earlier, rolled-back attempt on this context). Keeps the first switch-off time.
                await db.Charities
                    .Where(c => c.Id == id && c.DeletedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, DateTime.UtcNow));
                var tracked = db.Charities.Local.FirstOrDefault(c => c.Id == id);
                if (tracked != null) db.Entry(tracked).State = EntityState.Detached;

                var clearedDefaults = await db.Configurations
                    .Where(c => c.DefaultCharityId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DefaultCharityId, (Guid?)null));

                if (ownTx != null) await ownTx.CommitAsync();

                logger.LogInformation(
                    "Charity switched off for Id: {Id}; cleared {Count} default-charity settings", id, clearedDefaults);

                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete Charity Id: {Id}", id);
                throw;
            }
        }

        // Decommissioning switches the charity off exactly like Delete, and also stops all money flowing to
        // it. In one transaction, under the same FOR UPDATE lock on the charity row:
        //   - DeletedAt and DecommissionedAt are set (each keeps its first time);
        //   - every charge for it not yet taken (Pending, or claimed by a worker as Processing) is moved to
        //     Held, whether or not a payment intent was created for it. The drain never confirms a Held
        //     row: a worker still holding one is refused when it records a new payment intent (before the
        //     confirm), and a stale claim is never re-claimed. Held rows that already have a payment intent
        //     are only ever LOOKED UP (PenaltyService reconciles them): if the provider reports the money was
        //     taken anyway, the row becomes Charged and the money is held with the rest;
        //   - users' default-charity settings that point at it are cleared, as Delete does.
        // The one boundary: a confirm already on its way to the provider when this commits can still
        // succeed. Its money is recorded as Charged and held (payouts to this charity stop).
        // Nothing is erased. Returns the users to tell: those with a live habit giving to it, and those
        // whose default charity it was.
        public async Task<Result<CharityDecommissionResult?>> Decommission(
            Guid id, Func<string, Domain.Notification.InboxNotificationRecord> notice)
        {
            await using var ownTx = Transaction.Current == null && db.Database.CurrentTransaction == null
                ? await db.Database.BeginTransactionAsync()
                : null;
            try
            {
                logger.LogInformation("Decommissioning Charity Id: {Id}", id);

                // Serializes with a habit or default choosing it (FOR SHARE), with a penalty being enqueued for
                // it (FOR SHARE), and with Delete or another Decommission (FOR UPDATE), so none can slip past.
                // FOR NO KEY UPDATE, not FOR UPDATE: it does NOT conflict with the FOR KEY SHARE a foreign-key
                // check takes. A charge being recorded holds its penalty row and then inserts into tables that
                // reference this charity; blocking that insert while this waits for the penalty row would deadlock.
                var names = await db.Database
                    .SqlQuery<string>($@"SELECT ""Name"" AS ""Value"" FROM ""Charities"" WHERE ""Id"" = {id} FOR NO KEY UPDATE")
                    .ToListAsync();
                if (names.Count == 0)
                {
                    logger.LogWarning("Charity not found for decommission, Id: {Id}", id);
                    return (CharityDecommissionResult?)null;
                }

                var now = DateTime.UtcNow;
                await db.Charities
                    .Where(c => c.Id == id && c.DeletedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DeletedAt, now));
                await db.Charities
                    .Where(c => c.Id == id && c.DecommissionedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DecommissionedAt, now));
                var tracked = db.Charities.Local.FirstOrDefault(c => c.Id == id);
                if (tracked != null) db.Entry(tracked).State = EntityState.Detached;

                var held = await db.Penalties
                    .Where(p => p.CharityId == id
                                && (p.Status == (int)PenaltyStatus.Pending || p.Status == (int)PenaltyStatus.Processing))
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(p => p.Status, (int)PenaltyStatus.Held)
                        // From here Attempts counts provider look-ups of a Held row with an intent.
                        .SetProperty(p => p.Attempts, 0)
                        .SetProperty(p => p.LastError, PenaltyRepository.HeldReason)
                        .SetProperty(p => p.UpdatedAt, now));
                foreach (var p in db.Penalties.Local.Where(p => p.CharityId == id).ToList())
                    db.Entry(p).State = EntityState.Detached;

                var habitUsers = await (
                        from h in db.Habits
                        join v in db.HabitVersions on new { HabitId = h.Id, h.Version } equals new { v.HabitId, v.Version }
                        where h.DeletedAt == null && v.CharityId == id
                        select h.UserId)
                    .Distinct()
                    .ToListAsync();
                var defaultUsers = await db.Configurations
                    .Where(c => c.DefaultCharityId == id)
                    .Select(c => c.UserId)
                    .ToListAsync();

                await db.Configurations
                    .Where(c => c.DefaultCharityId == id)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.DefaultCharityId, (Guid?)null));

                // The in-app notices, in this same transaction, so a committed decommission always has them.
                // Decommissions of one charity serialize on the row lock above, so skipping users who already
                // have this notice (a repeat) cannot race another insert of the same dedupe key.
                var users = habitUsers.Concat(defaultUsers).Distinct().Order(StringComparer.Ordinal).ToList();
                var record = notice(names[0]);
                var alreadyTold = record.DedupeKey == null
                    ? []
                    : await db.InboxNotifications
                        .Where(n => n.DedupeKey == record.DedupeKey && users.Contains(n.UserId))
                        .Select(n => n.UserId)
                        .ToListAsync();
                var toTell = users.Except(alreadyTold).ToList();
                db.InboxNotifications.AddRange(toTell.Select(u => record.ToData(u)));
                await db.SaveChangesAsync();

                if (ownTx != null) await ownTx.CommitAsync();

                logger.LogInformation(
                    "Charity decommissioned for Id: {Id}; {Held} queued charges put on hold", id, held);

                return new CharityDecommissionResult
                {
                    CharityName = names[0],
                    AffectedUserIds = users,
                    HeldPenalties = held,
                    NoticesPosted = toTell.Count,
                };
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to decommission Charity Id: {Id}", id);
                throw;
            }
        }

        public async Task<Result<Unit?>> SetCauses(Guid id, IEnumerable<string> causeKeys)
        {
            try
            {
                logger.LogInformation("Setting Causes for Charity Id: {Id}", id);

                var charity = await db.Charities.FirstOrDefaultAsync(x => x.Id == id);
                if (charity == null)
                {
                  var ex = new EntityNotFound("Charity not found", typeof(Charity), id.ToString()).ToException();
                    logger.LogWarning(ex,"Charity not found for SetCauses, Id: {Id}", id);
                    
                    return ex;
                }

                var keys = causeKeys.Distinct().ToArray();
                var causeLookup = await db.Causes
                    .Where(c => keys.Contains(c.Key))
                    .Select(c => new { c.Id, c.Key })
                    .ToListAsync();

                var foundKeys = causeLookup.Select(x => x.Key).ToHashSet();
                var missingKeys = keys.Where(k => !foundKeys.Contains(k)).ToArray();
                if (missingKeys.Length > 0)
                {
                    logger.LogWarning("SetCauses missing keys: {Keys}", string.Join(",", missingKeys));
                    var errors = new Dictionary<string, string[]> { ["causeKeys"] = missingKeys };
                    return new ValidationError("Some causes not found", errors).ToException();
                }

                var desired = causeLookup.Select(x => x.Id).ToHashSet();
                var existing = await db.CharityCauses
                    .Where(cc => cc.CharityId == id)
                    .Select(cc => cc.CauseId)
                    .ToListAsync();
                var existingSet = existing.ToHashSet();

                var toAdd = desired.Except(existingSet).ToArray();
                var toRemove = existingSet.Except(desired).ToArray();

                if (toAdd.Length > 0)
                {
                    var rows = toAdd.Select(cid => new CharityCauseData { CharityId = id, CauseId = cid });
                    await db.CharityCauses.AddRangeAsync(rows);
                }

                if (toRemove.Length > 0)
                {
                    var rows = await db.CharityCauses
                        .Where(cc => cc.CharityId == id && toRemove.Contains(cc.CauseId))
                        .ToListAsync();
                    db.CharityCauses.RemoveRange(rows);
                }

                await db.SaveChangesAsync();
                logger.LogInformation("SetCauses completed for Charity Id: {Id}", id);
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to SetCauses for Charity Id: {Id}", id);
                throw;
            }
        }

        public async Task<Result<Unit?>> AddCause(Guid id, string causeKey)
        {
            try
            {
                logger.LogInformation("Adding Cause {CauseKey} to Charity Id: {Id}", causeKey, id);

                var charity = await db.Charities.FirstOrDefaultAsync(x => x.Id == id);
                if (charity == null)
                {
                    logger.LogWarning("Charity not found for AddCause, Id: {Id}", id);
                    return (Unit?)null;
                }

                var cause = await db.Causes.FirstOrDefaultAsync(c => c.Key == causeKey);
                if (cause == null)
                {
                    logger.LogWarning("AddCause cause not found: {Key}", causeKey);
                    var errors = new Dictionary<string, string[]> { ["causeKey"] = [causeKey] };
                    return new ValidationError("Cause not found", errors).ToException();
                }

                var exists = await db.CharityCauses
                    .AnyAsync(cc => cc.CharityId == id && cc.CauseId == cause.Id);

                if (!exists)
                {
                    var link = new CharityCauseData { CharityId = id, CauseId = cause.Id };
                    await db.CharityCauses.AddAsync(link);
                    await db.SaveChangesAsync();
                    logger.LogInformation("Added Cause {CauseKey} to Charity Id: {Id}", causeKey, id);
                }
                else
                {
                    logger.LogDebug("Cause {CauseKey} already linked to Charity Id: {Id}", causeKey, id);
                }

                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to AddCause {CauseKey} for Charity Id: {Id}", causeKey, id);
                throw;
            }
        }


        public async Task<Result<CharityPrincipal?>> GetByExternalId(string source, string externalKey)
        {
            try
            {
                logger.LogInformation("Get Charity by ExternalId: {Source}:{Key}", source, externalKey);
                var link = await db.ExternalIds
                    .Where(x => x.Source == source && x.ExternalKey == externalKey)
                    .FirstOrDefaultAsync();
                if (link == null) return (CharityPrincipal?)null;

                var charity = await db.Charities.FirstOrDefaultAsync(c => c.Id == link.CharityId);
                return charity?.ToPrincipal();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed GetByExternalId: {Source}:{Key}", source, externalKey);
                throw;
            }
        }

        public async Task<Result<Unit>> UpsertExternalId(Guid charityId, ExternalIdRecord external)
        {
            try
            {
                logger.LogInformation("Upserting ExternalId for Charity Id: {Id} — {Source}:{Key}", charityId, external.Source, external.ExternalKey);

                var charityExists = await db.Charities.AnyAsync(c => c.Id == charityId);
                if (!charityExists)
                {
                    logger.LogWarning("UpsertExternalId: Charity does not exist: {Id}", charityId);
                    return new EntityNotFound("Charity Not Found", typeof(Charity), charityId.ToString()).ToException();
                }

                var existing = await db.ExternalIds
                    .Where(x => x.Source == external.Source && x.ExternalKey == external.ExternalKey)
                    .FirstOrDefaultAsync();

                if (existing == null)
                {
                    var row = external.ToData(charityId);
                    db.ExternalIds.Add(row);
                    await db.SaveChangesAsync();
                    return new Unit();
                }

                if (existing.CharityId != charityId)
                {
                    logger.LogWarning("UpsertExternalId: conflict mapping {Source}:{Key} from {Old} to {New}", external.Source, external.ExternalKey, existing.CharityId, charityId);
                    return new EntityConflict("ExternalId is already linked to a different Charity", typeof(ExternalIdData)).ToException();
                }

                existing = existing.ToData(external);
                db.ExternalIds.Update(existing);
                await db.SaveChangesAsync();
                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed UpsertExternalId for Charity Id: {Id}", charityId);
                throw;
            }
        }

        public async Task<Result<BulkUpsertResult>> BulkUpsert(IEnumerable<BulkUpsertCharity> charities, CancellationToken ct = default)
        {
            try
            {
                var items = charities.ToArray();
                logger.LogInformation("Bulk upserting {Count} charities", items.Length);

                if (items.Length == 0)
                {
                    return new BulkUpsertResult
                    {
                        CharitiesCreated = 0,
                        CharitiesUpdated = 0,
                        ExternalIdsLinked = 0,
                        CausesLinked = 0
                    };
                }

                // STEP 1: Single query to fetch ALL existing external IDs with their charities
                var externalIds = items.Select(i => i.ExternalId.ExternalKey).Distinct().ToArray();
                var source = items.First().ExternalId.Source;

                // Validate all items share the same source
                if (items.Any(i => i.ExternalId.Source != source))
                {
                    throw new ArgumentException("All items in BulkUpsert must have the same ExternalId.Source");
                }

                var existingExtIds = await db.ExternalIds
                    .Where(e => e.Source == source && externalIds.Contains(e.ExternalKey))
                    .ToListAsync(ct);

                var charityIds = existingExtIds.Select(e => e.CharityId).Distinct().ToArray();
                var existingCharities = await db.Charities
                    .Where(c => charityIds.Contains(c.Id))
                    .ToListAsync(ct);

                var charityById = existingCharities.ToDictionary(c => c.Id);
                var existingByExtId = existingExtIds.ToDictionary(
                    e => e.ExternalKey,
                    e => charityById.GetValueOrDefault(e.CharityId)
                );

                // STEP 2: Prepare bulk insert/update collections
                var charitiesToCreate = new List<CharityData>();
                var charitiesToUpdate = new List<CharityData>();
                // Map externalKey -> tracked CharityData entity (Id is set after SaveChanges)
                var charityByExternalKey = new Dictionary<string, CharityData>();

                foreach (var item in items)
                {
                    var extKey = item.ExternalId.ExternalKey;
                    var existing = existingByExtId.GetValueOrDefault(extKey);

                    if (existing == null)
                    {
                        // Create new charity (Id generated by DB on SaveChanges)
                        var newCharity = item.Charity.ToData();
                        charitiesToCreate.Add(newCharity);
                        charityByExternalKey[extKey] = newCharity;
                    }
                    else
                    {
                        // Update existing charity
                        var updated = existing.ToData(item.Charity);
                        charitiesToUpdate.Add(updated);
                        charityByExternalKey[extKey] = updated;
                    }
                }

                // STEP 3: Bulk insert/update charities in SINGLE transaction
                if (charitiesToCreate.Count > 0)
                {
                    await db.Charities.AddRangeAsync(charitiesToCreate, ct);
                }
                if (charitiesToUpdate.Count > 0)
                {
                    db.Charities.UpdateRange(charitiesToUpdate);
                    // Sync never touches the switched-off or decommissioned flags: a stale save must not undo them.
                    foreach (var c in charitiesToUpdate)
                    {
                        db.Entry(c).Property(x => x.DeletedAt).IsModified = false;
                        db.Entry(c).Property(x => x.DecommissionedAt).IsModified = false;
                    }
                }
                await db.SaveChangesAsync(ct);

                var charitiesCreated = charitiesToCreate.Count;
                var charitiesUpdated = charitiesToUpdate.Count;

                // STEP 4: Bulk upsert external IDs
                var existingExtIdByKey = existingExtIds.ToDictionary(e => e.ExternalKey);
                var externalIdsToCreate = new List<ExternalIdData>();
                var externalIdsToUpdate = new List<ExternalIdData>();

                foreach (var item in items)
                {
                    var extKey = item.ExternalId.ExternalKey;
                    // After SaveChanges above, tracked CharityData has its Id populated
                    var charity = charityByExternalKey[extKey];
                    var charityId = charity.Id;
                    var existingExt = existingExtIdByKey.GetValueOrDefault(extKey);

                    if (existingExt == null)
                    {
                        externalIdsToCreate.Add(item.ExternalId.ToData(charityId));
                    }
                    else
                    {
                        externalIdsToUpdate.Add(existingExt.ToData(item.ExternalId));
                    }
                }

                if (externalIdsToCreate.Count > 0)
                {
                    await db.ExternalIds.AddRangeAsync(externalIdsToCreate, ct);
                }
                if (externalIdsToUpdate.Count > 0)
                {
                    db.ExternalIds.UpdateRange(externalIdsToUpdate);
                }
                await db.SaveChangesAsync(ct);

                var externalIdsLinked = externalIdsToCreate.Count + externalIdsToUpdate.Count;

                // STEP 5: Bulk fetch all cause keys and prepare cause links
                var allCauseKeys = items.SelectMany(i => i.CauseKeys).Distinct().ToArray();
                var causes = await db.Causes
                    .Where(c => allCauseKeys.Contains(c.Key))
                    .Select(c => new { c.Id, c.Key })
                    .ToListAsync(ct);

                var causeKeyToId = causes.ToDictionary(c => c.Key, c => c.Id);

                // Collect all charity-cause pairs we need
                var desiredPairs = new HashSet<(Guid charityId, Guid causeId)>();
                foreach (var item in items)
                {
                    var charityId = charityByExternalKey[item.ExternalId.ExternalKey].Id;
                    foreach (var causeKey in item.CauseKeys)
                    {
                        if (causeKeyToId.TryGetValue(causeKey, out var causeId))
                        {
                            desiredPairs.Add((charityId, causeId));
                        }
                    }
                }

                // STEP 6: Single query to fetch existing charity-cause links
                var charityIdsForLinks = charityByExternalKey.Values.Select(c => c.Id).Distinct().ToArray();
                var existingLinks = await db.CharityCauses
                    .Where(cc => charityIdsForLinks.Contains(cc.CharityId))
                    .Select(cc => new { cc.CharityId, cc.CauseId })
                    .ToListAsync(ct);

                var existingPairs = existingLinks
                    .Select(cc => (cc.CharityId, cc.CauseId))
                    .ToHashSet();

                // STEP 7: Bulk insert only missing charity-cause links
                var linksToCreate = desiredPairs
                    .Except(existingPairs)
                    .Select(pair => new CharityCauseData { CharityId = pair.Item1, CauseId = pair.Item2 })
                    .ToList();

                var causesLinked = 0;
                if (linksToCreate.Count > 0)
                {
                    await db.CharityCauses.AddRangeAsync(linksToCreate, ct);
                    await db.SaveChangesAsync(ct);
                    causesLinked = linksToCreate.Count;
                }

                logger.LogInformation(
                    "Bulk upsert completed: {Created} created, {Updated} updated, {ExternalIds} external IDs, {Causes} cause links",
                    charitiesCreated, charitiesUpdated, externalIdsLinked, causesLinked);

                return new BulkUpsertResult
                {
                    CharitiesCreated = charitiesCreated,
                    CharitiesUpdated = charitiesUpdated,
                    ExternalIdsLinked = externalIdsLinked,
                    CausesLinked = causesLinked
                };
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed bulk upsert");
                throw;
            }
        }
    }
}
