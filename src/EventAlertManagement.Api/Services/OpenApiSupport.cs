using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace EventAlertManagement.Api.Services;

/// <summary>
/// Marks every property that cannot be null as required, so generated clients get honest types
/// (<c>id: string</c> instead of <c>id?: string</c>). Without it a client has to guess what is always present.
/// </summary>
public sealed class RequiredNonNullableSchemaFilter : ISchemaFilter
{
    public void Apply(OpenApiSchema schema, SchemaFilterContext context)
    {
        if (schema.Properties is null || schema.Properties.Count == 0) return;
        foreach (var (name, property) in schema.Properties)
            if (!property.Nullable && !schema.Required.Contains(name)) schema.Required.Add(name);
    }
}

public static class SchemaNames
{
    /// <summary>Short readable names: <c>PagedResultOfRuleResponse</c> instead of a namespace-qualified generic.</summary>
    public static string For(Type t)
    {
        if (!t.IsGenericType) return t.Name;
        var name = t.Name[..t.Name.IndexOf('`')];
        return name + "Of" + string.Join("And", t.GetGenericArguments().Select(For));
    }
}
