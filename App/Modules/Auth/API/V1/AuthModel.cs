namespace App.Modules.Auth.API.V1;

public record WebHandoffReq(
  string Platform,
  string? Storefront
);

public record WebHandoffRes(
  string Url,
  int ExpiresInSeconds
);
