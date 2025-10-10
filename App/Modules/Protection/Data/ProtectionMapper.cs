using Domain.Protection;

namespace App.Modules.Protection.Data;

public static class ProtectionMapper
{
  public static UserProtectionRecord ToRecord(this UserProtectionData data)
  {
    return new UserProtectionRecord
    {
      FreezeCurrent = data.FreezeCurrent
    };
  }

  public static UserProtectionPrincipal ToPrincipal(this UserProtectionData data)
  {
    return new UserProtectionPrincipal
    {
      UserId = data.UserId,
      Record = data.ToRecord()
    };
  }
}

