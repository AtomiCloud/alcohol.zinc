using App.Utility;
using Domain.Vacation;

namespace App.Modules.Vacation.API.V1;

public static class VacationMapper
{
  public static VacationRecord ToRecord(this CreateVacationReq req)
  {
    return new VacationRecord
    {
      StartDate = req.StartDate.ToDate(),
      EndDate = req.EndDate.ToDate(),
      Timezone = req.Timezone
    };
  }

  public static VacationRes ToRes(this VacationPrincipal p)
  {
    return new VacationRes(
      p.Id,
      p.UserId,
      p.Record.StartDate.ToStandardDateFormat(),
      p.Record.EndDate.ToStandardDateFormat(),
      p.Record.Timezone,
      DateTime.UtcNow.ToString("O")
    );
  }

  public static VacationSearch ToDomain(this SearchVacationQuery query, string userId)
  {
    return new VacationSearch
    {
      UserId = userId,
      Year = query.Year,
      Limit = query.Limit ?? 50,
      Skip = query.Skip ?? 0
    };
  }
}
