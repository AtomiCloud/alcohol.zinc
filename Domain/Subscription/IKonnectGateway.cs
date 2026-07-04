using CSharp_Result;

namespace Domain.Subscription;

// Domain port over the Kong Konnect (OpenMeter) catalog mirror. Mirror-only:
// zinc's local table + config are the source of truth; a mirror failure must
// never fail a purchase (callers log and let the daily worker retry via the
// KonnectSyncedAt watermark). Konnect API specifics (paths, snake_case keys)
// live entirely in the App implementation, like IPaymentGateway for Airwallex.
public interface IKonnectGateway
{
  // Idempotent: returns the Konnect customer id for our userId (external key),
  // creating the customer if it does not exist yet.
  Task<Result<string>> UpsertCustomer(string userId);

  // Reflects the user's current tier/status onto Konnect: subscribes the
  // customer to the plan matching `tier`, replacing/cancelling any previous
  // subscription. `status` is zinc's status string for labelling.
  Task<Result<Unit>> UpsertSubscription(
    string konnectCustomerId, string tier, DateTime periodStart, DateTime periodEnd, string status);
}
