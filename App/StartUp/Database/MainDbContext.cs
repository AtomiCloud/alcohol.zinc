using App.Modules.Causes.Data;
using App.Modules.Charities.Data;
using App.Modules.Check.Data;
using App.Modules.Configurations.Data;
using App.Modules.Disbursement.Data;
using App.Modules.Friend.Data;
using App.Modules.FriendStake.Data;
using App.Modules.Habit.Data;
using App.Modules.HabitExecution.Data;
using App.Modules.HabitVersion.Data;
using App.Modules.NfcTag.Data;
using App.Modules.Notification.Data;
using App.Modules.Payment.Data;
using App.Modules.Penalty.Data;
using App.Modules.Proof.Data;
using App.Modules.Protection.Data;
using App.Modules.Routine.Data;
using App.Modules.Strava.Data;
using App.Modules.Subscription.Data;
using App.Modules.Users.Data;
using App.Modules.Vacation.Data;
using App.StartUp.Options;
using App.StartUp.Services;
using EntityFramework.Exceptions.PostgreSQL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace App.StartUp.Database;

public class MainDbContext(IOptionsMonitor<Dictionary<string, DatabaseOption>> options, ILoggerFactory factory)
  : DbContext
{
  public const string Key = "MAIN";


  public DbSet<UserData> Users { get; set; }
  public DbSet<ConfigurationData> Configurations { get; set; }
  public DbSet<CharityData> Charities { get; set; }
  public DbSet<CauseData> Causes { get; set; }
  public DbSet<CharityCauseData> CharityCauses { get; set; }
  public DbSet<ExternalIdData> ExternalIds { get; set; }
  public DbSet<RoutineData> Routines { get; set; }
  public DbSet<HabitData> Habits { get; set; }
  public DbSet<HabitVersionData> HabitVersions { get; set; }
  public DbSet<HabitExecutionData> HabitExecutions { get; set; }
  // Daily checks, the evidence that settled them, and the settled-check outbox
  public DbSet<HabitCheckData> HabitChecks { get; set; }
  public DbSet<HabitEvidenceData> HabitEvidence { get; set; }
  public DbSet<CheckEventData> CheckEvents { get; set; }
  public DbSet<CheckEventDeliveryData> CheckEventDeliveries { get; set; }
  public DbSet<PaymentCustomerData> PaymentCustomers { get; set; }
  public DbSet<PaymentConsentData> PaymentConsents { get; set; }
  // Protections & Vacation
  public DbSet<VacationPeriodData> VacationPeriods { get; set; }
  public DbSet<UserProtectionData> UserProtections { get; set; }
  public DbSet<FreezeAwardData> FreezeAwards { get; set; }
  public DbSet<FreezeConsumptionData> FreezeConsumptions { get; set; }
  // Penalty
  public DbSet<PenaltyData> Penalties { get; set; }
  public DbSet<CharityBalanceData> CharityBalances { get; set; }
  // Disbursement (charity payout)
  public DbSet<DisbursementData> Disbursements { get; set; }
  // Subscription (paid tiers)
  public DbSet<UserSubscriptionData> UserSubscriptions { get; set; }
  // NFC tags (physical tag → habit mapping)
  public DbSet<NfcTagData> NfcTags { get; set; }
  // Proof plugins bound to habits (one per habit)
  public DbSet<ProofBindingData> ProofBindings { get; set; }
  // Strava proof plugin: each user's link, and the webhook inbox
  public DbSet<StravaConnectionData> StravaConnections { get; set; }
  public DbSet<StravaEventData> StravaEvents { get; set; }
  // Friends, the in-app inbox, and the "tell my friends if I miss" stake
  public DbSet<FriendRequestData> FriendRequests { get; set; }
  public DbSet<FriendshipData> Friendships { get; set; }
  public DbSet<FriendInviteData> FriendInvites { get; set; }
  public DbSet<InboxNotificationData> InboxNotifications { get; set; }
  public DbSet<HabitFriendWatcherData> HabitFriendWatchers { get; set; }
  // public DbSet<CompletionData> Completions { get; set; }
  // public DbSet<StatsData> Stats { get; set; }

  protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
  {
    optionsBuilder
      .UseLoggerFactory(factory)
      .AddPostgres(options.CurrentValue, Key)
      .UseExceptionProcessor();
  }

  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    var user = modelBuilder.Entity<UserData>();
    user.HasIndex(x => x.Username).IsUnique();

    // Configuration
    var configuration = modelBuilder.Entity<ConfigurationData>();
    configuration.HasIndex(c => c.UserId).IsUnique(); // One configuration per user
    configuration.HasOne(c => c.Charity)
                 .WithMany()
                 .HasForeignKey(c => c.DefaultCharityId)
                 .OnDelete(DeleteBehavior.Cascade);

    // Habit configuration (main entity)
    var habit = modelBuilder.Entity<HabitData>();
    habit.HasIndex(h => h.Version);
    habit.HasIndex(h => h.UserId);
    // Lists read a user's habits in this order. Not unique: ties are broken by Id.
    habit.HasIndex(h => new { h.UserId, h.SortOrder });
    habit.HasMany(h => h.Versions)
         .WithOne(hv => hv.Habit)
         .HasForeignKey(hv => hv.HabitId);

    // Routine configuration: every habit belongs to a routine. Habits.RoutineId stays
    // nullable (expand/contract: pods on the previous release insert habits without it).
    // Deletes are soft, so never cascade.
    var routine = modelBuilder.Entity<RoutineData>();
    routine.HasIndex(r => r.UserId);
    routine.HasMany(r => r.Habits)
           .WithOne(h => h.Routine)
           .HasForeignKey(h => h.RoutineId)
           .IsRequired(false)
           .OnDelete(DeleteBehavior.Restrict);

    // HabitVersion configuration (versioned details)
    var habitVersion = modelBuilder.Entity<HabitVersionData>();
    habitVersion.HasIndex(x => new { x.HabitId, x.Version }).IsUnique();  // Unique version per habit
    habitVersion.HasOne(hv => hv.Charity)
                .WithMany()
                .HasForeignKey(hv => hv.CharityId)
                .OnDelete(DeleteBehavior.Cascade);
    habitVersion.HasMany(hv => hv.Executions)
                .WithOne(he => he.HabitVersion)
                .HasForeignKey(he => he.HabitVersionId);

    // HabitExecution configuration
    var habitExecution = modelBuilder.Entity<HabitExecutionData>();
    habitExecution.HasIndex(x => new { x.HabitVersionId, x.Date }).IsUnique();  // One execution per version per day

    // Checks: exactly one per habit per local date; settled once (guarded in CheckRepository.Settle).
    // Cascades only fire on hard deletes (account purge); habit deletion is a soft delete.
    var habitCheck = modelBuilder.Entity<HabitCheckData>();
    habitCheck.HasIndex(x => new { x.HabitId, x.LocalDate }).IsUnique();
    habitCheck.HasIndex(x => new { x.Status, x.DeadlineUtc }); // CloseDue scan
    habitCheck.HasIndex(x => x.ExecutionId).IsUnique();
    habitCheck.HasOne(x => x.Habit)
              .WithMany()
              .HasForeignKey(x => x.HabitId)
              .OnDelete(DeleteBehavior.Cascade);
    habitCheck.HasOne(x => x.Execution)
              .WithMany()
              .HasForeignKey(x => x.ExecutionId)
              .OnDelete(DeleteBehavior.Cascade);

    var evidence = modelBuilder.Entity<HabitEvidenceData>();
    evidence.HasIndex(x => new { x.HabitId, x.ProofKey, x.ExternalId }).IsUnique(); // Report idempotency
    evidence.HasIndex(x => new { x.ProofKey, x.ExternalId }); // Strava activity deleted
    evidence.HasOne<HabitCheckData>()
            .WithMany()
            .HasForeignKey(x => x.CheckId)
            .OnDelete(DeleteBehavior.Cascade);

    var checkEvent = modelBuilder.Entity<CheckEventData>();
    checkEvent.HasIndex(x => new { x.DispatchedAtUtc, x.NextAttemptAtUtc }); // dispatcher claim scan
    checkEvent.HasOne<HabitCheckData>()
              .WithMany()
              .HasForeignKey(x => x.CheckId)
              .OnDelete(DeleteBehavior.Cascade);

    var delivery = modelBuilder.Entity<CheckEventDeliveryData>();
    delivery.HasIndex(x => new { x.EventId, x.BindingId }).IsUnique(); // a stake fires at most once per event
    delivery.HasOne<CheckEventData>()
            .WithMany()
            .HasForeignKey(x => x.EventId)
            .OnDelete(DeleteBehavior.Cascade);

    // Proof bindings: one per habit. The cascade covers hard deletes (account purge);
    // a soft-deleted habit's binding stays but is never routed to.
    var proofBinding = modelBuilder.Entity<ProofBindingData>();
    proofBinding.HasIndex(x => x.HabitId).IsUnique();
    proofBinding.HasIndex(x => x.PluginKey);
    proofBinding.HasOne(x => x.Habit)
                .WithMany()
                .HasForeignKey(x => x.HabitId)
                .OnDelete(DeleteBehavior.Cascade);

    // Strava: one connection per user and per Strava athlete. Cascades on the account purge.
    var stravaConnection = modelBuilder.Entity<StravaConnectionData>();
    stravaConnection.HasIndex(x => x.UserId).IsUnique();
    stravaConnection.HasIndex(x => x.AthleteId).IsUnique();
    stravaConnection.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

    var stravaEvent = modelBuilder.Entity<StravaEventData>();
    stravaEvent.HasIndex(x => x.ReceivedAtUtc); // Claim scan

    // Friends: one pending request per ordered pair, one friendship row per direction. Every
    // user reference cascades, so deleting an account removes their side of everything.
    var friendRequest = modelBuilder.Entity<FriendRequestData>();
    friendRequest.HasIndex(x => new { x.FromUserId, x.ToUserId }).IsUnique();
    friendRequest.HasIndex(x => x.ToUserId);
    friendRequest.HasOne(x => x.From).WithMany().HasForeignKey(x => x.FromUserId).OnDelete(DeleteBehavior.Cascade);
    friendRequest.HasOne(x => x.To).WithMany().HasForeignKey(x => x.ToUserId).OnDelete(DeleteBehavior.Cascade);

    var friendship = modelBuilder.Entity<FriendshipData>();
    friendship.HasIndex(x => new { x.UserId, x.FriendUserId }).IsUnique();
    friendship.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    friendship.HasOne(x => x.FriendUser).WithMany().HasForeignKey(x => x.FriendUserId).OnDelete(DeleteBehavior.Cascade);

    var friendInvite = modelBuilder.Entity<FriendInviteData>();
    friendInvite.HasIndex(x => x.UserId).IsUnique();
    friendInvite.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

    // Inbox: newest-first per user; DedupeKey makes outbox retries post once.
    var inbox = modelBuilder.Entity<InboxNotificationData>();
    inbox.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
    inbox.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique().HasFilter("\"DedupeKey\" IS NOT NULL");
    inbox.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    inbox.HasOne(x => x.Actor).WithMany().HasForeignKey(x => x.ActorUserId).OnDelete(DeleteBehavior.Cascade);

    // Friend stake: one row per (habit, friend). Habit deletes are soft; the cascade covers purges.
    var watcher = modelBuilder.Entity<HabitFriendWatcherData>();
    watcher.HasIndex(x => new { x.HabitId, x.FriendUserId }).IsUnique();
    watcher.HasIndex(x => x.FriendUserId);
    watcher.HasOne(x => x.Habit).WithMany().HasForeignKey(x => x.HabitId).OnDelete(DeleteBehavior.Cascade);
    watcher.HasOne(x => x.FriendUser).WithMany().HasForeignKey(x => x.FriendUserId).OnDelete(DeleteBehavior.Cascade);

    // Charity configuration
    var charity = modelBuilder.Entity<CharityData>();
    charity.HasIndex(c => c.Name);
    charity.HasIndex(c => new { c.PrimaryRegistrationCountry, c.PrimaryRegistrationNumber });
    charity.HasIndex(c => c.Countries).HasMethod("gin");

    // Cause configuration
    var cause = modelBuilder.Entity<CauseData>();
    cause.HasIndex(c => c.Key).IsUnique();

    // CharityCause configuration (many-to-many via explicit join)
    var charityCause = modelBuilder.Entity<CharityCauseData>();
    charityCause.HasIndex(cc => new { cc.CharityId, cc.CauseId }).IsUnique();
    charityCause.HasOne<CharityData>()
                .WithMany()
                .HasForeignKey(cc => cc.CharityId)
                .OnDelete(DeleteBehavior.Cascade);
    charityCause.HasOne<CauseData>()
                .WithMany()
                .HasForeignKey(cc => cc.CauseId)
                .OnDelete(DeleteBehavior.Cascade);

    // ExternalId configuration
    var externalId = modelBuilder.Entity<ExternalIdData>();
    externalId.HasIndex(e => new { e.Source, e.ExternalKey }).IsUnique();
    externalId.HasOne<CharityData>()
              .WithMany()
              .HasForeignKey(e => e.CharityId)
              .OnDelete(DeleteBehavior.Cascade);

    // PaymentCustomer configuration
    var paymentCustomer = modelBuilder.Entity<PaymentCustomerData>();
    paymentCustomer.HasIndex(x => x.UserId).IsUnique();  // One payment customer per user
    paymentCustomer.HasIndex(x => x.AirwallexCustomerId);

    // Purpose-scoped consents: at most one per (customer, purpose).
    var paymentConsent = modelBuilder.Entity<PaymentConsentData>();
    paymentConsent.HasIndex(x => new { x.PaymentCustomerId, x.Purpose }).IsUnique();
    paymentConsent.HasOne(x => x.PaymentCustomer)
                  .WithMany(x => x.Consents)
                  .HasForeignKey(x => x.PaymentCustomerId)
                  .OnDelete(DeleteBehavior.Cascade);

    // NFC tags: natural key = tag id from the physical tag's URL. The cascades
    // cover hard deletes (user purge); habit deletion is a soft delete, so
    // HabitRepository.Delete releases tags explicitly.
    var nfcTag = modelBuilder.Entity<NfcTagData>();
    nfcTag.HasIndex(x => x.UserId);
    nfcTag.HasOne(x => x.User)
          .WithMany()
          .HasForeignKey(x => x.UserId)
          .OnDelete(DeleteBehavior.Cascade);
    nfcTag.HasOne(x => x.Habit)
          .WithMany()
          .HasForeignKey(x => x.HabitId)
          .OnDelete(DeleteBehavior.Cascade);

    // VacationPeriods
    var vacation = modelBuilder.Entity<VacationPeriodData>();
    vacation.HasIndex(x => x.UserId);
    vacation.HasIndex(x => new { x.UserId, x.StartDate });

    // Protections
    var protection = modelBuilder.Entity<UserProtectionData>();
    protection.HasKey(x => x.Id);

    var award = modelBuilder.Entity<FreezeAwardData>();
    award.HasIndex(x => new { x.HabitId, x.WeekStart }).IsUnique();

    var consume = modelBuilder.Entity<FreezeConsumptionData>();
    consume.HasIndex(x => new { x.UserId, x.Date }).IsUnique();

    // Penalty
    var penalty = modelBuilder.Entity<PenaltyData>();
    penalty.HasIndex(x => x.HabitExecutionId).IsUnique();   // exactly-once per execution
    penalty.HasIndex(x => new { x.Status, x.Attempts });    // drain query: GetPending
    penalty.HasIndex(x => new { x.Status, x.DisbursementId }); // pending-payout scan: Charged + DisbursementId IS NULL
    penalty.HasOne(x => x.Charity)
           .WithMany()
           .HasForeignKey(x => x.CharityId)
           .OnDelete(DeleteBehavior.Cascade);
    // Payout linkage. SetNull (not Cascade): deleting a disbursement row must never
    // delete the underlying penalties — it just releases them back to pending-payout.
    penalty.HasOne<DisbursementData>()
           .WithMany()
           .HasForeignKey(x => x.DisbursementId)
           .OnDelete(DeleteBehavior.SetNull);

    // Disbursement (charity payout ledger): one row per (charity, currency) payout attempt.
    var disbursement = modelBuilder.Entity<DisbursementData>();
    disbursement.HasIndex(x => x.Status); // worker scan: pending/reconcilable disbursements
    disbursement.HasOne(x => x.Charity)
                .WithMany()
                .HasForeignKey(x => x.CharityId)
                .OnDelete(DeleteBehavior.Cascade);

    var charityBalance = modelBuilder.Entity<CharityBalanceData>();
    // One accrual row per (charity, currency): a charity can legitimately receive
    // penalties in multiple currencies, so the running total is kept per currency
    // rather than forcing a single currency per charity.
    charityBalance.HasIndex(x => new { x.CharityId, x.Currency }).IsUnique();
    charityBalance.HasOne(x => x.Charity)
                  .WithMany()
                  .HasForeignKey(x => x.CharityId)
                  .OnDelete(DeleteBehavior.Cascade);

    // Subscription (paid tiers): one row per user.
    var subscription = modelBuilder.Entity<UserSubscriptionData>();
    subscription.HasIndex(x => x.UserId).IsUnique();
    subscription.HasIndex(x => new { x.Status, x.PeriodEnd }); // renewal scan: GetDue
    subscription.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
  }
}
