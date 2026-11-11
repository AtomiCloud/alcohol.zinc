namespace Domain.Exceptions;

// A request the domain refuses as invalid: field -> messages (400 ValidationError).
public class InvalidRequestException(Dictionary<string, string[]> errors) : Exception("Invalid request")
{
  public InvalidRequestException(string field, string message) : this(new() { [field] = [message] })
  {
  }

  public Dictionary<string, string[]> Errors { get; } = errors;
}
