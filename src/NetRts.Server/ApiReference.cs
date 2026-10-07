using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using NetRts.Server.Auth;
using Scalar.AspNetCore;

namespace NetRts.Server;

/// <summary>OpenAPI document plus the Scalar API reference/playground at /scalar.</summary>
public static class ApiReference
{
    private const string SchemeName = "ApiKey";

    public static IServiceCollection AddNetRtsOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "NetRts API";
                document.Info.Description =
                    "Bots play NetRts over this API. Register with POST /api/v1/players to get an API key, " +
                    "then send it as `Authorization: Bearer <key>`. The full rules are in GET /api/v1/rules.";
                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes[SchemeName] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "nrts_…",
                    Description = "Your API key from POST /api/v1/players.",
                };
                return Task.CompletedTask;
            });

            // Mark endpoints that need a key, so the playground sends it.
            options.AddOperationTransformer((operation, context, _) =>
            {
                var requiresAuth = context.Description.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>().Any();
                if (requiresAuth)
                {
                    operation.Security ??= [];
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference(SchemeName, context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });

    public static WebApplication MapNetRtsApiReference(this WebApplication app)
    {
        app.MapOpenApi();
        app.MapScalarApiReference("/scalar", options => options
            .WithTitle("NetRts API")
            .AddPreferredSecuritySchemes(SchemeName)
            .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient));

        // Old links keep working.
        app.MapGet("/swagger", () => Results.Redirect("/scalar")).ExcludeFromDescription();
        return app;
    }
}
