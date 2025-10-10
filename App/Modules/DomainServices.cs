using App.Modules.Allowance;
using App.Modules.Causes.Data;
using App.Modules.Charities.Data;
using App.Modules.Charities.Sync;
using App.Modules.Configurations.Data;
using App.Modules.Entitlement;
using App.Modules.Habit.Data;
using App.Modules.Payment.Airwallex;
using App.Modules.Payment.Data;
using App.Modules.Protection.Data;
using App.Modules.System;
using App.Modules.Users.Data;
using App.Modules.Vacation;
using App.Modules.Vacation.Data;
using App.StartUp.Services;
using App.StartUp.Services.Subscription;
using Domain;
using Domain.Allowance;
using Domain.Cause;
using Domain.Charity;
using Domain.Configuration;
using Domain.Entitlement;
using Domain.Habit;
using Domain.Payment;
using Domain.Protection;
using Domain.Subscription;
using Domain.User;
using Domain.Vacation;

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

    // CAUSE
    s.AddScoped<ICauseService, CauseService>()
      .AutoTrace<ICauseService>();

    s.AddScoped<ICauseRepository, CauseRepository>()
      .AutoTrace<ICauseRepository>();

    // CONFIGURATION
    s.AddScoped<IConfigurationService, ConfigurationService>()
      .AutoTrace<IConfigurationService>();

    s.AddScoped<IConfigurationRepository, ConfigurationRepository>()
      .AutoTrace<IConfigurationRepository>();

    // HABIT
    s.AddScoped<IHabitService, HabitService>()
      .AutoTrace<IHabitService>();

    s.AddScoped<IHabitRepository, HabitRepository>()
      .AutoTrace<IHabitRepository>();

    // HABIT: Overview
    s.AddScoped<IHabitOverviewService, HabitOverviewService>()
      .AutoTrace<IHabitOverviewService>();

    // HABIT: Streaks
    s.AddScoped<IStreakService, StreakService>()
      .AutoTrace<IStreakService>();
    s.AddScoped<IStreakRepository, StreakRepository>()
      .AutoTrace<IStreakRepository>();

    // Protections & Vacation
    s.AddScoped<IProtectionRepository, ProtectionRepository>()
      .AutoTrace<IProtectionRepository>();
    s.AddScoped<IVacationRepository, VacationRepository>()
      .AutoTrace<IVacationRepository>();
    s.AddScoped<IVacationService, VacationService>()
      .AutoTrace<IVacationService>();

    // Allowance utilities
    s.AddScoped<IAllowanceService, AllowanceService>()
      .AutoTrace<IAllowanceService>();

    s.AddScoped<IEntitlementService, EntitlementService>()
      .AutoTrace<IEntitlementService>();

    // SUBSCRIPTION (temporary stub until Lagos integration)
    s.AddScoped<ISubscriptionService, NullSubscriptionService>()
      .AutoTrace<ISubscriptionService>();
    // PAYMENT
    s.AddScoped<IPaymentService, PaymentService>()
      .AutoTrace<IPaymentService>();

    s.AddScoped<IPaymentCustomerRepository, PaymentCustomerRepository>()
      .AutoTrace<IPaymentCustomerRepository>();

    s.AddScoped<IPaymentGateway, AirwallexGateway>()
      .AutoTrace<IPaymentGateway>();

    s.AddScoped<AirwallexClient>();
    s.AddScoped<IAirwallexAuthenticator, AirwallexAuthenticator>();
    s.AddScoped<AirwallexWebhookService>();
    s.AddScoped<AirwallexEventAdapter>();
    s.AddScoped<AirwallexHmacCalculator>();

    // Transaction Manager
    s.AddScoped<ITransactionManager, TransactionManager>()
      .AutoTrace<ITransactionManager>();

    s.AddScoped<IEncryptor, Encryptor>()
      .AutoTrace<IEncryptor>();

    // Pledge sync
    s.AddScoped<IPledgeClient, PledgeClient>()
      .AutoTrace<IPledgeClient>();
    s.AddScoped<IPledgeSyncService, PledgeSyncService>()
      .AutoTrace<IPledgeSyncService>();



    return s;
  }
}
