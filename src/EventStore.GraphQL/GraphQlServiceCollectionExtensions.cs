using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace EventStore.GraphQL;

// ADR-037 -- HotChocolate (docs/libraries/dotnet/hotchocolate.md), the
// concrete server behind the GraphQL Gateway. Depth/cost limiting is
// mandatory, not optional (this ADR's own Decision text) -- both configured
// here, never left at HotChocolate's own defaults. Exact API verified via
// reflection against the actual installed v16 assemblies before writing this
// (docs/libraries' own "verify before citing" -- an older-version doc
// sample's AddMaxExecutionDepthRule/ModifyCostOptions shapes did not
// perfectly match what's actually callable on 16.5.1).
public static class GraphQlServiceCollectionExtensions
{
    public static IServiceCollection AddEventStoreGraphQl(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        // ASP.NET Core doesn't register ClaimsPrincipal in DI by default --
        // both a resolver's own `ClaimsPrincipal user` parameter (HotChocolate's
        // well-known-parameter binding, RegistryQueries/LineageQueries/
        // RevealFieldMutation) and `ctx.Service<ClaimsPrincipal>()`
        // (FollowSubscriptionTypeModule's SubscribeAsync, which has no such
        // parameter-binding convention available on a raw IResolverContext)
        // need the SAME real caller identity, so this registers it once,
        // uniformly, rather than two different resolution paths.
        services.AddScoped(sp => sp.GetRequiredService<IHttpContextAccessor>().HttpContext!.User);

        services
            .AddScoped<Follow.Api.EventTailReader>()
            .AddScoped<Lineage.Api.LineageService>()
            .AddScoped<ViewRegistry.ViewDefinitionService>()
            .AddSingleton<FollowSubscriptionTypeModule>()
            .AddSingleton<Domain.SchemaRegistry.ISchemaChangeNotifier>(sp => sp.GetRequiredService<FollowSubscriptionTypeModule>())
            .AddSingleton<EntityQueryTypeModule>()
            .AddGraphQLServer()
            .AddQueryType<Query>()
            .AddTypeExtension<RegistryQueries>()
            .AddTypeExtension<PendingTaskQueries>()
            .AddTypeExtension<LineageQueries>()
            .AddTypeExtension<LineageExportQueries>()
            .AddTypeExtension<CapabilitiesQueries>()
            .AddTypeExtension<ViewDefinitionQueries>()
            .AddMutationType<Mutation>()
            .AddTypeExtension<RevealFieldMutation>()
            .AddTypeExtension<ViewDefinitionMutation>()
            .AddSubscriptionType<Subscription>()
            .AddTypeModule<FollowSubscriptionTypeModule>()
            .AddTypeModule<EntityQueryTypeModule>()
            .AddType<MaskedString>()
            .AddType<MaskedFloat>()
            .AddType<MaskedBoolean>()
            .AddType<MaskedDateTimeOffset>()
            .AddType<Attachment>()
            .AddType<EventFilterInput>()
            .AddType<Follow.Api.FollowMode>()
            .AddDiagnosticEventListener<GraphQlDiagnosticEventListener>() // duplex.graphql.* metrics -- see that class's own comment
            .AddMaxExecutionDepthRule(15) // guards against unbounded hierarchical fan-out (e.g. deeply nested ancestors-of-ancestors)
            .AddCostAnalyzer()
            .ModifyCostOptions(o => o.MaxFieldCost = 10_000) // mandatory complexity/cost scoring, independent of depth
            .ModifyRequestOptions(o => o.IncludeExceptionDetails = true); // dev/POC posture throughout this repo (plaintext DevIdp secrets, etc.) -- a masked "Unexpected Execution Error" with no detail at all cost real debugging time building this item

        return services;
    }
}
