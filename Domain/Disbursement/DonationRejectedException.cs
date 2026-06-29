namespace Domain.Disbursement;

// Signals that the donation provider DEFINITIVELY refused the request (a 4xx client error), so no
// donation was created. This is the only failure where it is safe to release the penalties for a
// fresh retry. Any other failure (5xx, timeout, network, unparseable body) is AMBIGUOUS — the
// donation may have landed — and must leave the disbursement Pending for reconciliation instead,
// otherwise releasing the penalties risks a double payout (Pledge has no idempotency key).
public class DonationRejectedException : Exception
{
  public DonationRejectedException(string? message) : base(message) { }
  public DonationRejectedException(string? message, Exception? innerException) : base(message, innerException) { }
}
