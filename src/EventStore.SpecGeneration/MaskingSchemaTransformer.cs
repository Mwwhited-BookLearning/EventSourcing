using Microsoft.OpenApi;

namespace EventStore.SpecGeneration;

// docs/06-solution-structure.md, "Spec generation" section: schema-level,
// claims-independent -- NOT the same thing as the later, data-level
// IPayloadMasker. Needed the moment AsyncApiDocumentBuilder exists, so a
// maskable property never appears in a generated document as a bare,
// unwrapped type.
public class MaskingSchemaTransformer
{
    public OpenApiSchema Wrap(OpenApiSchema schema) => (OpenApiSchema)WrapNode(schema);

    private static IOpenApiSchema WrapNode(IOpenApiSchema node)
    {
        if (node is not OpenApiSchema schema)
            return node;

        if (schema.Properties is { } properties)
            foreach (var name in properties.Keys.ToList())
                properties[name] = IsMaskable(properties[name])
                    ? WrapMaskable((OpenApiSchema)properties[name])
                    : WrapNode(properties[name]);

        if (schema.Items is { } items)
            schema.Items = WrapNode(items);

        return schema;
    }

    private static bool IsMaskable(IOpenApiSchema node) =>
        node is OpenApiSchema { Extensions: { } extensions } && extensions.ContainsKey("x-masking");

    private static OpenApiSchema WrapMaskable(OpenApiSchema inner) => new()
    {
        OneOf =
        [
            new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema> { ["value"] = inner },
                Required = new HashSet<string> { "value" },
            },
            new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema> { ["masked"] = new OpenApiSchema { Type = JsonSchemaType.Boolean } },
                Required = new HashSet<string> { "masked" },
            },
            new OpenApiSchema
            {
                Type = JsonSchemaType.Object,
                Properties = new Dictionary<string, IOpenApiSchema> { ["erased"] = new OpenApiSchema { Type = JsonSchemaType.Boolean } },
                Required = new HashSet<string> { "erased" },
            },
        ],
    };
}
