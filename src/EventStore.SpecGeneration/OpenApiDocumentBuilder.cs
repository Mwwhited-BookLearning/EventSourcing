using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EventStore.Domain.SchemaRegistry;
using EventStore.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.OpenApi;

namespace EventStore.SpecGeneration;

// docs/06-solution-structure.md's "Spec generation" section (ADR-002):
// IMemoryCache-backed with a ~60s TTL, invalidated by SchemaRegistryService
// on the next successful registration (see AddSchemaRegistry's cache
// invalidation call) rather than a background refresh timer.
public class OpenApiDocumentBuilder(EventStoreContext db, EventSchemaConverter converter, IMemoryCache cache)
{
    public const string CacheKey = "openapi-document";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(60);

    public async Task<string> GetOrBuildJsonAsync(CancellationToken ct = default)
    {
        if (cache.TryGetValue(CacheKey, out string? cached))
            return cached!;

        var json = await BuildJsonAsync(ct);
        cache.Set(CacheKey, json, CacheDuration);
        return json;
    }

    private async Task<string> BuildJsonAsync(CancellationToken ct)
    {
        var document = new OpenApiDocument
        {
            Info = new OpenApiInfo { Title = "EventStore Publish API", Version = "1.0.0" },
            Paths = new OpenApiPaths(),
        };

        var activeTypes = await db.EventTypeDefinitions
            .AsNoTracking()
            .Where(e => e.IsActive)
            .ToListAsync(ct);

        foreach (var type in activeTypes)
        {
            var envelopeSchema = BuildEnvelopeSchema(type.JsonSchema);
            var operation = new OpenApiOperation
            {
                Summary = $"Publish a {type.Name} event",
                Extensions = BuildRequiredClaimsExtension(type.RequiredClaims, ClaimDirection.Publish),
                RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, IOpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = envelopeSchema },
                    },
                },
                // ADR-023's persist-everything rebuild changed this endpoint's
                // whole response shape (PublishResult.cs is the authoritative
                // list of cases) -- this generated spec had drifted back to
                // the pre-rebuild "200/201, content can be rejected" framing,
                // found stale by a design-compliance audit. 202 (not 201) is
                // ADR-011/023's own explicit correction: every syntactically-
                // parseable, authorized, non-conflicting publish is Accepted
                // regardless of schema/entity validity, which the async
                // Router determines afterward and never gates this response
                // on -- there is no "content is invalid" rejection left to
                // document as a 400 here.
                Responses = new OpenApiResponses
                {
                    ["202"] = new OpenApiResponse { Description = "Accepted (or an identical-content idempotent replay) -- schema/entity conformance is determined asynchronously by the Router and never gates this response" },
                    ["400"] = new OpenApiResponse { Description = "An unresolved Strict-mode parent, or (RequiredSignature configured) a satisfied step-up with no Meaning supplied" },
                    ["401"] = new OpenApiResponse { Description = "RequiredSignature configured and the caller's authentication strength doesn't satisfy it (RFC 9470 step-up challenge)" },
                    ["403"] = new OpenApiResponse { Description = "Caller lacks a Publish-direction RequiredClaims entry, or the events:publish scope itself" },
                    ["404"] = new OpenApiResponse { Description = "Event type not registered" },
                    ["409"] = new OpenApiResponse { Description = "eventId already used with different content" },
                },
            };

            var pathItem = new OpenApiPathItem();
            pathItem.AddOperation(HttpMethod.Post, operation);
            document.Paths[$"/publish/{type.Name}"] = pathItem;
        }

        using var stream = new MemoryStream();
        await using (var streamWriter = new StreamWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            var writer = new OpenApiJsonWriter(streamWriter);
            document.SerializeAsV31(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    // ADR-050 -- "x-required-claims at the schema/operation level," the
    // entity-level counterpart to ADR-009's already-emitted property-level
    // "x-masking": a reader of the generated spec (Scalar, ADR-025) can
    // see directly which claim this operation requires, without registry
    // access. RequiredClaims itself (the internal enforcement mechanism,
    // RequiredClaimEvaluator) is unaffected -- this only guarantees the
    // SAME already-computed list also reaches the rendered document, per
    // this ADR's own "guarantee it's emitted, not just tracked internally"
    // framing. Absent entirely when a type declares no RequiredClaims at
    // all, rather than an empty array -- consistent with x-masking's own
    // "extension key present only when it's actually maskable" convention.
    internal static Dictionary<string, IOpenApiExtension>? BuildRequiredClaimsExtension(List<RequiredClaim> requiredClaims, ClaimDirection direction)
    {
        var matching = requiredClaims.Where(c => c.Direction == direction).ToList();
        if (matching.Count == 0)
            return null;

        var claims = new JsonArray(matching.Select(c => (JsonNode)c.Claim).ToArray());
        return new Dictionary<string, IOpenApiExtension> { ["x-required-claims"] = new JsonNodeExtension(claims) };
    }

    private OpenApiSchema BuildEnvelopeSchema(string payloadSchemaText)
    {
        // Built by parsing a hand-assembled JSON Schema fragment through the
        // same converter EventSchemaConverter uses, rather than constructing
        // OpenApiSchema property-by-property -- one proven code path for
        // "arbitrary schema-shaped JSON text in, OpenApiSchema out."
        var envelope = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject
            {
                ["schemaVersion"] = new JsonObject { ["type"] = "integer" },
                ["payload"] = JsonNode.Parse(payloadSchemaText),
                ["parentEventIds"] = new JsonObject
                {
                    ["type"] = "array",
                    ["items"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
                },
                ["eventId"] = new JsonObject { ["type"] = "string", ["format"] = "uuid" },
            },
            ["required"] = new JsonArray("schemaVersion", "payload"),
        };
        return converter.Parse(envelope.ToJsonString());
    }
}
