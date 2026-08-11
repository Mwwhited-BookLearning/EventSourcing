using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
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
                RequestBody = new OpenApiRequestBody
                {
                    Required = true,
                    Content = new Dictionary<string, IOpenApiMediaType>
                    {
                        ["application/json"] = new OpenApiMediaType { Schema = envelopeSchema },
                    },
                },
                Responses = new OpenApiResponses
                {
                    ["201"] = new OpenApiResponse { Description = "Created (or an identical-content idempotent replay)" },
                    ["400"] = new OpenApiResponse { Description = "Unknown schemaVersion, non-conforming payload, or an unresolved Strict-mode parent" },
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
