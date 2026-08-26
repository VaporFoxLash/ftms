using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FTMS.Api.Serialization;

/// <summary>
/// Describes money as a plain JSON number in the OpenAPI document.
///
/// By default .NET describes a decimal as <c>type: ["number", "string"]</c>, because
/// System.Text.Json is willing to READ a decimal that arrives quoted. That is honest about the
/// reader but misleading about the contract: FTMS always WRITES an unquoted number, and the
/// union propagates into every generated client as <c>amount: number | string</c>, forcing each
/// consumer to narrow a case that never occurs.
///
/// design: doc 05 section 9 - OpenAPI is the single client facing contract and both clients
/// generate their API layers from it, so an imprecise schema is a real cost paid by every
/// caller. The wire format is unchanged; only its description is corrected.
/// </summary>
internal sealed class DecimalSchemaTransformer : IOpenApiSchemaTransformer
{
    public Task TransformAsync(
        OpenApiSchema schema,
        OpenApiSchemaTransformerContext context,
        CancellationToken cancellationToken)
    {
        var type = context.JsonTypeInfo.Type;

        if (type == typeof(decimal) || type == typeof(decimal?))
        {
            schema.Type = type == typeof(decimal?)
                ? JsonSchemaType.Number | JsonSchemaType.Null
                : JsonSchemaType.Number;

            // The pattern only described the string form we just removed.
            schema.Pattern = null;
        }

        return Task.CompletedTask;
    }
}
