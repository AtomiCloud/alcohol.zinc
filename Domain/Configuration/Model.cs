namespace Domain.Configuration

{
    public record ConfigurationModel
    {
        public required string Sub { get; set; }
        public required string Timezone { get; set; }
        public TimeOnly EndOfDay { get; set; }
        public int? DefaultCharityId { get; set; }
    }
}

