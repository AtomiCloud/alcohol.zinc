using App.Error.V1;
using App.StartUp.Database;
using App.Utility;
using CSharp_Result;
using Domain.Charity;
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

                IQueryable<CharityData> query = db.Charities.AsQueryable();

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

        public async Task<Result<Unit?>> Delete(Guid id)
        {
            try
            {
                logger.LogInformation("Deleting Charity Id: {Id}", id);

                var data = await db.Charities.FirstOrDefaultAsync(x => x.Id == id);
                if (data == null)
                {
                    logger.LogWarning("Charity not found for delete, Id: {Id}", id);
                    return (Unit?)null;
                }
                db.Charities.Remove(data);
                await db.SaveChangesAsync();

                logger.LogInformation("Charity deleted for Id: {Id}", id);

                return new Unit();
            }
            catch (Exception e)
            {
                logger.LogError(e, "Failed to delete Charity Id: {Id}", id);
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
