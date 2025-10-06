namespace Domain.Charity
{
    public record ExternalIdRecord
    {
        public required string Source { get; init; }
        public required string ExternalKey { get; init; }
        public string? Url { get; init; }
        public string? Payload { get; init; }
        public DateTimeOffset? LastSyncedAt { get; init; }
    }
}

