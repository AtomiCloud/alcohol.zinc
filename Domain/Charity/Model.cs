namespace Domain.Charity
{
    public class CharityRecord
    {
      public required string Name { get; set; }
      public required string Email { get; set; }
      public required string Address { get; set; }
    }
    
    public class CharityPrincipal
    {
      public required Guid Id { get; set; }
      public required CharityRecord Record { get; set; }
    }

    public class Charity
    {
      public required CharityPrincipal Principal { get; set; }
    }
}
