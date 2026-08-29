namespace EventStore.Domain.SchemaRegistry;

public class FilterableField
{
    public int Id { get; set; }
    public string EventTypeAppId { get; set; } = default!;
    public string EventTypeName { get; set; } = default!;
    public int EventTypeVersion { get; set; }
    public string JsonPath { get; set; } = default!;
    public FilterableFieldType DataType { get; set; }
    public bool IsIndexed { get; set; }

    // ADR-096/ADR-097 -- PlaintextExpression (default) is every field
    // registered before these ADRs, completely unchanged. Set only when
    // this JsonPath's schema node also carries x-masking-searchable.
    public FilterableFieldIndexKind IndexKind { get; set; } = FilterableFieldIndexKind.PlaintextExpression;
    public SearchableIndexConfig? SearchableConfig { get; set; }
}
