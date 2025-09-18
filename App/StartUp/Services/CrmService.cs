using App.StartUp.Services.Crm;
using App.StartUp.Services.Crm.HubSpot;

namespace App.StartUp.Services;

public static class CrmService
{
  public static IServiceCollection AddCrmService(this IServiceCollection services)
  {
    services.AddScoped<ICrmManagement, HubSpotCrmManagement>()
      .AutoTrace<ICrmManagement>();
    return services;
  }
}

