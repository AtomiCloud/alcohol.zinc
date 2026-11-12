using System.Reflection;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace App.StartUp.Services.Swagger;

public class RequireNonNullablePropertiesSchemaFilter : ISchemaFilter
{
  /// <summary>
  /// Add to model.Required all properties where Nullable is false, except those the C# type
  /// declares nullable (`T?`). Swashbuckle never marks a $ref property nullable, so a nullable
  /// one becomes `allOf: [$ref], nullable: true` to tell clients it can be null.
  /// </summary>
  public void Apply(OpenApiSchema model, SchemaFilterContext context)
  {
    var nullability = new NullabilityInfoContext();
    var declaredNullable = context.Type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p => nullability.Create(p).ReadState == NullabilityState.Nullable)
      .Select(p => p.Name)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    foreach (var (key, property) in model.Properties.ToList())
    {
      if (declaredNullable.Contains(key))
      {
        if (property.Reference != null)
          model.Properties[key] = new OpenApiSchema { AllOf = [property], Nullable = true };
        continue;
      }

      if (!property.Nullable && !model.Required.Contains(key))
        model.Required.Add(key);
    }
  }
}
