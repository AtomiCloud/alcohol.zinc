using System.Globalization;
using App.Modules.Common;
using App.StartUp.Services.Auth;
using Asp.Versioning;
using CSharp_Result;
using Domain.Penalty;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace App.Modules.Penalty.API.V1;

[ApiVersion(1.0)]
[ApiController]
[Route("api/v{version:apiVersion}/[controller]")]
public class PenaltyController(IPenaltyRepository penalties, IAuthHelper authHelper)
  : AtomiControllerBase(authHelper)
{
  // What the user still owes, oldest first: the same days the overview's totalDebt sums. Items that
  // could not be charged are charged again once the user adds or fixes their card.
  [Authorize, HttpGet("{userId}/owed")]
  public async Task<ActionResult<OwedRes>> Owed(string userId)
  {
    var result = await this.GuardAsync(userId)
      .ThenAwait(_ => penalties.ListOwed(userId))
      .Then(items => items.ToRes(), Errors.MapNone);
    return this.ReturnResult(result);
  }
}

public record OwedRes(
  string Total,        // sum of items, "F2"
  string? Currency,    // currency of the total; null when nothing is owed
  List<OwedItemRes> Items
);

public record OwedItemRes(
  Guid HabitId,
  string HabitName,
  string LocalDate,    // yyyy-MM-dd, the habit's local day
  string Amount,       // "F2"
  string Currency,
  Guid? CharityId,
  string? CharityName,
  string State,        // "charging" | "couldNotCharge"
  string? Reason       // couldNotCharge only: "noCard" | "chargeFailed"
);

public static class OwedMapper
{
  public static OwedRes ToRes(this List<OwedItem> items) => new(
    items.Sum(i => i.Amount).ToString("F2", CultureInfo.InvariantCulture),
    items.FirstOrDefault()?.Currency,
    items.Select(i => new OwedItemRes(
      i.HabitId,
      i.HabitName,
      i.LocalDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
      i.Amount.ToString("F2", CultureInfo.InvariantCulture),
      i.Currency,
      i.CharityId,
      i.CharityName,
      i.CouldNotCharge == null ? "charging" : "couldNotCharge",
      i.CouldNotCharge switch
      {
        OwedReason.NoCard => "noCard",
        OwedReason.ChargeFailed => "chargeFailed",
        _ => null
      })).ToList());
}
