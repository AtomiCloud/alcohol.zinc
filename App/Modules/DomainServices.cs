using App.Modules.Allowance;
using App.Modules.Causes.Data;
using App.Modules.Charities.Data;
using App.Modules.Charities.Sync;
using App.Modules.Configurations.Data;
using App.Modules.Entitlement;
using App.Modules.Habit.Data;
using App.Modules.NfcTag;
using App.Modules.NfcTag.Data;
using App.Modules.Notification;
using App.Modules.Payment.Airwallex;
using App.Modules.Payment.Data;
using App.Modules.Penalty.Data;
using App.Modules.Protection.Data;
using App.Modules.Subscription;
using App.Modules.Subscription.Data;
using App.Modules.System;
using App.Modules.Users.Data;
using App.Modules.Vacation;
using App.Modules.Vacation.Data;
using App.StartUp.Services;
using Domain;
using Domain.Allowance;
using Domain.Cause;
using Domain.Charity;
using Domain.Configuration;
using Domain.Disbursement;
using Domain.Entitlement;
using Domain.Habit;
using Domain.NfcTag;
using Domain.Notification;
using Domain.Payment;
using Domain.Penalty;
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
    s.AddScoped<IFreezePolicy, App.Modules.Protection.FreezePolicy>()
      .AutoTrace<IFreezePolicy>();
    s.AddScoped<Domain.Protection.IProtectionAwardService, App.Modules.Protection.ProtectionAwardService>()
      .AutoTrace<Domain.Protection.IProtectionAwardService>();
    s.AddScoped<IVacationRepository, VacationRepository>()
      .AutoTrace<IVacationRepository>();
    s.AddScoped<IVacationService, VacationService>()
      .AutoTrace<IVacationService>();

    // NFC tags
    s.AddScoped<INfcTagRepository, NfcTagRepository>()
      .AutoTrace<INfcTagRepository>();
    s.AddScoped<INfcTagService, NfcTagService>()
      .AutoTrace<INfcTagService>();

    // PENALTY
    s.AddScoped<IPenaltyService, PenaltyService>()
      .AutoTrace<IPenaltyService>();
    s.AddScoped<IPenaltyRepository, PenaltyRepository>()
      .AutoTrace<IPenaltyRepository>();

    // Allowance utilities
    s.AddScoped<IAllowanceService, AllowanceService>()
      .AutoTrace<IAllowanceService>();

    s.AddScoped<IEntitlementService, EntitlementService>()
      .AutoTrace<IEntitlementService>();

    // SUBSCRIPTION (local table + config catalog)
    s.AddScoped<ISubscriptionService, Domain.Subscription.SubscriptionService>()
      .AutoTrace<ISubscriptionService>();

    s.AddScoped<ISubscriptionManagementService, SubscriptionManagementService>()
      .AutoTrace<ISubscriptionManagementService>();

    s.AddScoped<ISubscriptionRepository, SubscriptionRepository>()
      .AutoTrace<ISubscriptionRepository>();

    s.AddScoped<ISubscriptionPlanProvider, SubscriptionPlanProvider>()
      .AutoTrace<ISubscriptionPlanProvider>();


    // WEB HANDOFF (neon app -> web billing portal magic link)
    s.AddScoped<Auth.IWebHandoffService, Auth.WebHandoffService>()
      .AutoTrace<Auth.IWebHandoffService>();
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

    // NOTIFICATION (best-effort transactional email)
    s.AddScoped<IEmailNotifier, EmailNotifier>()
      .AutoTrace<IEmailNotifier>();

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

    // DISBURSEMENT (charity payout)
    s.AddScoped<IDisbursementService, Domain.Disbursement.DisbursementService>()
      .AutoTrace<IDisbursementService>();
    s.AddScoped<IDisbursementRepository, App.Modules.Disbursement.Data.DisbursementRepository>()
      .AutoTrace<IDisbursementRepository>();
    s.AddScoped<IDonationGateway, App.Modules.Disbursement.PledgeDonationGateway>()
      .AutoTrace<IDonationGateway>();



    return s;
  }
}
