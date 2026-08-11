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
}
