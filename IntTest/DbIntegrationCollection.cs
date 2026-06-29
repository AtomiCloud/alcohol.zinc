namespace IntTest;

// All DB-backed integration test classes share ONE Postgres (from PENALTY_TEST_DB) and manage
// its schema with EnsureCreated/EnsureDeleted in their lifecycle. xUnit runs distinct test
// classes in parallel by default, so two such classes would race on the same database (one
// dropping it mid-run of the other). Assigning them to a single collection makes xUnit run them
// serially. New DB-backed int classes should carry [Collection(DbIntegrationCollection.Name)].
[CollectionDefinition(Name)]
public class DbIntegrationCollection
{
  public const string Name = "DbIntegration";
}
