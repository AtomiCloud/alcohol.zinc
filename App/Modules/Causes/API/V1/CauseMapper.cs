using Domain.Cause;

namespace App.Modules.Causes.API.V1;

public static class CauseMapper
{
  // RES
  public static CausePrincipalRes ToRes(this CausePrincipal cause)
    => new(cause.Id.ToString(), cause.Record.Key, cause.Record.Name);

  public static CauseRes ToRes(this Cause cause)
    => new(cause.Principal.ToRes());

  // REQ
  public static CauseRecord ToRecord(this CreateCauseReq req)
    => new() { Key = req.Key, Name = req.Name };

  public static CauseRecord ToRecord(this UpdateCauseReq req)
    => new() { Key = string.Empty, Name = req.Name };

  public static CauseSearch ToDomain(this CauseSearchReq req)
    => new() { Key = req.Key, Name = req.Name, Limit = req.Limit ?? 20, Skip = req.Skip ?? 0 };
}
