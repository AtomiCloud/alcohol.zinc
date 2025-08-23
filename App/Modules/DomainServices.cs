using App.Modules.Charities.Data;
using App.Modules.Configurations.Data;
using App.Modules.System;
using App.Modules.Users.Data;
using App.StartUp.Services;
using Domain;
using Domain.Charity;
using Domain.Configuration;
using Domain.User;

namespace App.Modules;

public static class DomainServices
{
  public static IServiceCollection AddDomainServices(this IServiceCollection s)
  {
    // USER
    s.AddScoped<IUserService, UserService>()
      .AutoTrace<IUserService>();

    s.AddScoped<IUserRepository, UserRepository>()
      .AutoTrace<IUserRepository>();

    // CHARITY
    s.AddScoped<ICharityService, CharityService>()
      .AutoTrace<ICharityService>();

    s.AddScoped<ICharityRepository, CharityRepository>()
      .AutoTrace<ICharityRepository>();

    // CONFIGURATION
    s.AddScoped<IConfigurationService, ConfigurationService>()
      .AutoTrace<IConfigurationService>();

    s.AddScoped<IConfigurationRepository, ConfigurationRepository>()
      .AutoTrace<IConfigurationRepository>();

    // Transaction Manager
    s.AddScoped<ITransactionManager, TransactionManager>()
      .AutoTrace<ITransactionManager>();

    s.AddScoped<IEncryptor, Encryptor>()
      .AutoTrace<IEncryptor>();



    return s;
  }
}
