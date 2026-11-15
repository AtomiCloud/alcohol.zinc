using System.Text.Json.Serialization;
using NJsonSchema.Annotations;

namespace App.Error.V1;

public class FriendRequestIdRequired : IDomainProblem
{
  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "friend_request_id_required";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Friend Request ID Required";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "Update LazyTax to respond to this friend request.";
}

public class FriendRequestResolved : IDomainProblem
{
  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "friend_request_resolved";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Friend Request Resolved";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "This friend request has already been resolved.";
}

public class FriendRequestWrongRole : IDomainProblem
{
  [JsonIgnore, JsonSchemaIgnore] public string Id { get; } = "friend_request_wrong_role";
  [JsonIgnore, JsonSchemaIgnore] public string Title { get; } = "Friend Request Wrong Role";
  [JsonIgnore, JsonSchemaIgnore] public string Version { get; } = "v1";
  [JsonIgnore, JsonSchemaIgnore] public string Detail { get; } = "You cannot make this decision on this friend request.";
}
