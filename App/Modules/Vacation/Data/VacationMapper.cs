using Domain.Vacation;

namespace App.Modules.Vacation.Data;

public static class VacationMapper
{
  public static VacationPeriodData ToData(this VacationRecord record, string userId)
  {
    return new VacationPeriodData
    {
      UserId = userId,
      StartDate = record.StartDate,
      EndDate = record.EndDate,
      Timezone = record.Timezone
    };
  }

  public static VacationPeriodData ToData(this VacationPeriodData data, VacationRecord record)
  {
    data.StartDate = record.StartDate;
    data.EndDate = record.EndDate;
    data.Timezone = record.Timezone;
    return data;
  }

  public static VacationRecord ToRecord(this VacationPeriodData data)
  {
    return new VacationRecord
    {
      StartDate = data.StartDate,
      EndDate = data.EndDate,
      Timezone = data.Timezone
    };
  }

  public static VacationPrincipal ToPrincipal(this VacationPeriodData data)
  {
    return new VacationPrincipal
    {
      Id = data.Id,
      UserId = data.UserId,
      Record = data.ToRecord()
    };
  }
}

