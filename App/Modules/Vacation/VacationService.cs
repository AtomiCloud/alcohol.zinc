using App.Error.V1;
using App.Utility;
using CSharp_Result;
using Domain.Allowance;
using Domain.Entitlement;
using Domain.Exceptions;
using Domain.Vacation;

namespace App.Modules.Vacation;

public class VacationService(
  IVacationRepository repo,
  IAllowanceService allowanceService,
  IEntitlementService entitlementService
) : IVacationService
{
  public Task<Result<VacationPrincipal>> Create(string userId, VacationRecord record)
  {
    return Validate(record)
      .ThenAwait(_ => repo.HasOverlap(userId, record.StartDate, record.EndDate))
      .Then(overlap => overlap
        ? (Result<VacationRecord>)new ValidationError(
            "Invalid CreateVacationReq",
            new Dictionary<string, string[]> { ["Range"] = [ "Vacation window overlaps with an existing one" ] }
          ).ToException()
        : record.ToResult())
      .ThenAwait(rec => entitlementService.VacationWindowQuota(userId)
        .ThenAwait(windows => repo.Create(userId, rec, windows)));
  }

  public Task<Result<List<VacationPrincipal>>> Search(VacationSearch search)
  {
    return repo.Search(search);
  }

  public Task<Result<Unit?>> Delete(string userId, Guid vacationId)
  {
    return repo.Get(vacationId, userId)
      .ThenAwait(async v =>
      {
        if (v == null)
          return (Result<Unit?>)new NotFoundException("Vacation Not Found", typeof(VacationPrincipal), vacationId.ToString());

        // Vacation dates are user-local days, in the user's current timezone like every check.
        var userToday = await allowanceService.GetUserToday(userId);
        if (userToday.IsFailure()) return (Result<Unit?>)userToday.FailureOrDefault();
        var today = userToday.Get().Today;
        if (today >= v.Record.StartDate)
          return (Result<Unit?>)new ValidationError(
            "Invalid DeleteVacation",
            new Dictionary<string, string[]> { ["State"] = [ "Cannot delete a vacation that has started" ] }
          ).ToException();

        return await repo.Delete(vacationId, userId);
      });
  }

  public Task<Result<VacationPrincipal?>> EndToday(string userId, Guid vacationId)
  {
    return repo.Get(vacationId, userId)
      .ThenAwait(async v =>
      {
        if (v == null)
          return (Result<VacationPrincipal?>)new NotFoundException("Vacation Not Found", typeof(VacationPrincipal), vacationId.ToString());

        var userToday = await allowanceService.GetUserToday(userId);
        if (userToday.IsFailure()) return (Result<VacationPrincipal?>)userToday.FailureOrDefault();
        var today = userToday.Get().Today;
        if (today < v.Record.StartDate)
          return (Result<VacationPrincipal?>)new ValidationError(
            "Invalid EndVacation",
            new Dictionary<string, string[]> { ["State"] = [ "Cannot end a vacation that hasn't started" ] }
          ).ToException();

        var newRecord = new VacationRecord
        {
          StartDate = v.Record.StartDate,
          EndDate = today < v.Record.EndDate ? today : v.Record.EndDate,
          Timezone = v.Record.Timezone
        };
        return await repo.Update(vacationId, newRecord);
      });
  }

  private static Task<Result<Unit>> Validate(VacationRecord record)
  {
    if (record.StartDate > record.EndDate)
    {
      return Task.FromResult((Result<Unit>)new ValidationError(
        "Invalid CreateVacationReq",
        new Dictionary<string, string[]> { ["StartDate/EndDate"] = [ "StartDate must be before or equal to EndDate" ] }
      ).ToException());
    }
    return Task.FromResult((Result<Unit>)new Unit());
  }
}
