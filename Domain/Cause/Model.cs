namespace Domain.Cause
{
    public record CauseSearch
    {
        public string? Key { get; init; }
        public string? Name { get; init; }
        public int Limit { get; init; }
        public int Skip { get; init; }
    }

    public class CauseRecord
    {
        public required string Key { get; init; }
        public required string Name { get; init; }
    }

    public class CausePrincipal
    {
        public required Guid Id { get; init; }
        public required CauseRecord Record { get; init; }
    }

    public class Cause
    {
        public required CausePrincipal Principal { get; init; }
    }
}
