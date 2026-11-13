namespace Domain.Entitlement;

// A counted limit of the user's plan. The repository that writes counts against it in the same
// transaction, under the user's lock, so concurrent requests can't both pass the same count.
public record Quota(string Tier, string Key, int Limit);
