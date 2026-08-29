namespace EventStore.Domain.SchemaRegistry;

// ADR-096/ADR-097 -- the parsed, persisted form of a property's
// x-masking-searchable schema extension, snapshotted onto FilterableField
// at registration time (the same "parsed once, reused at query/publish
// time" shape RequiredSignature/ExpectedResponse already establish on
// EventTypeDefinition itself). PayloadIndexer independently re-parses the
// raw x-masking-searchable JSON from the schema at publish time (the same
// "no shared schema walker" pattern PayloadEncryptor/PayloadMasker/
// MaskingSchemaValidator already each follow) -- this snapshot exists for
// GraphQlFilterPredicateBuilder's query-routing use, which only ever has a
// FilterableField in hand, never the raw schema.
public class SearchableIndexConfig
{
    public SearchableIndexKind IndexKind { get; set; }
    public SearchIndexKeyScope KeyScope { get; set; }
    public List<string>? BucketGranularities { get; set; } // Range only -- e.g. ["Year","Month","Day"] or numeric bucket widths ["10","100"]
    public FieldCardinality? Cardinality { get; set; }      // required for Range -- drives the ADR-096 registration guardrail
    public bool AcknowledgeLeakageRisk { get; set; }        // Range + Low cardinality + regulatoryClassification present: required to register at all. Never accepted for OrderRevealing (ADR-097) -- that combination is refused outright, no override.
}
