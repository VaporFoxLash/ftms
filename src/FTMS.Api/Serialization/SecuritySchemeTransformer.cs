using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace FTMS.Api.Serialization;

/// <summary>
/// Declares the bearer scheme in the OpenAPI document, and marks which operations require it.
///
/// Without this the published contract described every endpoint as anonymous while the code
/// required a token on all but three of them. That is not a cosmetic gap: the document is the
/// single source both clients generate from (design: doc 05 section 9), so the generated Angular
/// client had no notion that an Authorization header existed, and a reviewer opening Swagger UI
/// got no Authorize button and a wall of 401s.
/// </summary>
internal sealed class SecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    internal const string SchemeName = "bearerAuth";

    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();

        document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description =
                "Paste the accessToken returned by POST /api/auth/login. "
                + "Tokens last 15 minutes; POST /api/auth/refresh issues a new one from the "
                + "session cookie.",
        };

        return Task.CompletedTask;
    }
}

/// <summary>
/// Applies the bearer requirement to the operations that actually enforce it.
///
/// Per operation rather than a single document level requirement, because three endpoints are
/// genuinely anonymous - login, refresh and health - and a blanket requirement would describe
/// them wrongly. The authoritative signal is the endpoint metadata itself, so the document
/// cannot drift from the attributes: adding [AllowAnonymous] to an action updates the contract
/// with no second edit here.
/// </summary>
internal sealed class SecurityRequirementTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var metadata = context.Description.ActionDescriptor.EndpointMetadata;

        var isAnonymous = metadata.OfType<IAllowAnonymous>().Any();
        var requiresAuthorization = metadata.OfType<IAuthorizeData>().Any();

        if (isAnonymous || !requiresAuthorization)
        {
            return Task.CompletedTask;
        }

        operation.Security =
        [
            new OpenApiSecurityRequirement
            {
                // The host document must be passed, or the reference has nothing to resolve
                // against and serialises as an empty requirement object - which reads as
                // "this operation requires no security" rather than "requires bearerAuth".
                [new OpenApiSecuritySchemeReference(SecuritySchemeTransformer.SchemeName, context.Document)] = [],
            },
        ];

        return Task.CompletedTask;
    }
}
