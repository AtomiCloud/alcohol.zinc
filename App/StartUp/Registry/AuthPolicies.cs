namespace App.StartUp.Registry;

public class AuthPolicies
{
  public const string OnlyAdmin = "OnlyAdmin";

  public const string Registered = "Registered";
}

public class AuthRoles
{
  public const string Field = "scope";
  public const string Admin = "admin";
}

public class LogtoClaims
{
  public const string ZincUpdated = "alcohol_zinc";
  public const string ConfigurationId = "configuration_id";
}
