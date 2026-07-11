using CSharp_Result;

namespace Domain.NfcTag;

public interface INfcTagService
{
  /// <summary>
  /// Create-or-replace the tag → habit mapping. Creates the mapping when the
  /// tag is unclaimed, updates it when the caller already owns it, and fails
  /// with EntityConflict when another user owns it (first-come-first-served).
  /// </summary>
  Task<Result<NfcTagPrincipal>> Link(string userId, string tagId, Guid habitId);

  /// <summary>
  /// Resolve a tag to its habit's current version and today's execution
  /// status. Returns null (not found) when the tag is unclaimed or owned by
  /// another user — ownership is never leaked.
  /// </summary>
  Task<Result<NfcTagResolution?>> Resolve(string userId, string tagId);

  Task<Result<Unit?>> Unlink(string userId, string tagId);
}
