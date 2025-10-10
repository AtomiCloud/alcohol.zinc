namespace App.Modules.Vacation.API.V1;

public record CreateVacationReq(
  string StartDate,
  string EndDate,
  string Timezone
);

public record VacationRes(
  Guid Id,
  string UserId,
  string StartDate,
  string EndDate,
  string Timezone,
  string CreatedAt
);

public record SearchVacationQuery(
  int? Year,
  int? Limit,
  int? Skip
);

