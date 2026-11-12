using System.Text.Json;
using App.Modules.Configurations.API.V1;
using App.Modules.Habit.API.V1;
using App.StartUp.Services.Swagger;
using FluentValidation;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace UnitTest.Charity;

// A charity is needed only for a habit with a money stake: the app no longer asks for a
// default charity up front, and free habits have none.
public class OptionalCharityTests
{
  private static readonly Guid Charity = Guid.Parse("10000000-0000-0000-0000-000000000001");

  [Theory]
  [InlineData("0", null, true)]
  [InlineData("0.00", "00000000-0000-0000-0000-000000000000", true)]
  [InlineData("0", "10000000-0000-0000-0000-000000000001", true)]
  [InlineData("0.01", null, false)]
  [InlineData("5", "00000000-0000-0000-0000-000000000000", false)]
  [InlineData("5", "10000000-0000-0000-0000-000000000001", true)]
  public void Habit_NeedsACharity_OnlyWithAStake(string stake, string? charityId, bool valid)
  {
    Guid? charity = charityId == null ? null : Guid.Parse(charityId);
    var create = new CreateHabitReqValidator().Validate(
      new CreateHabitReq("Run 5 km", ["Monday"], "19:00:00", stake, charity, "Asia/Singapore"));
    var update = new UpdateHabitReqValidator().Validate(
      new UpdateHabitReq("Run 5 km", ["Monday"], "19:00:00", stake, charity, true, "Asia/Singapore"));

    foreach (var result in new[] { create, update })
    {
      result.IsValid.Should().Be(valid, string.Join("; ", result.Errors));
      if (!valid) result.Errors.Select(e => e.PropertyName).Should().Equal(nameof(CreateHabitReq.CharityId));
    }
  }

  // An unparsable stake is a Stake error only, not also a missing charity.
  [Fact]
  public void Habit_BadStake_IsNotReportedAsAMissingCharity()
  {
    new CreateHabitReqValidator().Validate(
        new CreateHabitReq("Run 5 km", ["Monday"], "19:00:00", "abc", null, "Asia/Singapore"))
      .Errors.Select(e => e.PropertyName).Should().OnlyContain(p => p == nameof(CreateHabitReq.Stake));
  }

  [Fact]
  public void Habit_NullOrEmptyCharity_IsStoredAsNone()
  {
    new CreateHabitReq("Run", ["Monday"], "19:00:00", "0", Guid.Empty, "UTC").ToVersionRecord().CharityId.Should().BeNull();
    new UpdateHabitReq("Run", ["Monday"], "19:00:00", "0", null, true, "UTC").ToVersionRecord().CharityId.Should().BeNull();
    new CreateHabitReq("Run", ["Monday"], "19:00:00", "5", Charity, "UTC").ToVersionRecord().CharityId.Should().Be(Charity);
  }

  [Theory]
  [InlineData(null, true)]
  [InlineData("", true)]
  [InlineData("10000000-0000-0000-0000-000000000001", true)]
  [InlineData("not-a-guid", false)]
  public void Configuration_DefaultCharity_IsOptional(string? charityId, bool valid)
  {
    new CreateConfigurationReqValidator().Validate(new CreateConfigurationReq("Asia/Singapore", charityId))
      .IsValid.Should().Be(valid);
    new UpdateConfigurationReqValidator().Validate(new UpdateConfigurationReq("Asia/Singapore", charityId))
      .IsValid.Should().Be(valid);
  }

  [Fact]
  public void Configuration_WithoutCharity_HasNoDefault()
  {
    new CreateConfigurationReq("Asia/Singapore").ToRecord().DefaultCharityId.Should().BeNull();
    new UpdateConfigurationReq("Asia/Singapore", "").ToRecord().DefaultCharityId.Should().BeNull();
    new UpdateConfigurationReq("Asia/Singapore", Charity.ToString()).ToRecord().DefaultCharityId.Should().Be(Charity);
  }

  // The app generates its models from the swagger, and its parsers throw on null for a
  // required field: what can be null must be optional and marked nullable there.
  private static SchemaRepository Spec(params Type[] types)
  {
    var generator = new SchemaGenerator(
      new SchemaGeneratorOptions { SchemaFilters = { new RequireNonNullablePropertiesSchemaFilter() } },
      new JsonSerializerDataContractResolver(new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    var repo = new SchemaRepository();
    foreach (var t in types) generator.GenerateSchema(t, repo);
    return repo;
  }

  [Theory]
  [InlineData(typeof(ConfigurationRes), "ConfigurationRes", "charity", "principal")]
  [InlineData(typeof(HabitOverviewHabitRes), "HabitOverviewHabitRes", "charity", "status")]
  public void Spec_NullableObjectField_IsNullableAndNotRequired(Type type, string schema, string optional,
    string required)
  {
    var s = Spec(type).Schemas[schema];
    s.Required.Should().Contain(required).And.NotContain(optional);
    s.Properties[optional].Nullable.Should().BeTrue();
    s.Properties[optional].AllOf.Should().ContainSingle().Which.Reference.Should().NotBeNull();
  }

  [Theory]
  [InlineData(typeof(HabitVersionRes), "charityId")]
  [InlineData(typeof(CreateHabitReq), "charityId")]
  [InlineData(typeof(UpdateHabitReq), "charityId")]
  [InlineData(typeof(ConfigurationPrincipalRes), "defaultCharityId")]
  [InlineData(typeof(CreateConfigurationReq), "defaultCharityId")]
  [InlineData(typeof(UpdateConfigurationReq), "defaultCharityId")]
  public void Spec_CharityIds_AreNullableAndNotRequired(Type type, string property)
  {
    var s = Spec(type).Schemas[type.Name];
    s.Required.Should().NotContain(property);
    s.Properties[property].Nullable.Should().BeTrue();
  }
}
