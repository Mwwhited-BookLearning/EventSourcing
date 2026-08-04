namespace EventStore.GraphQL;

// Empty root -- every actual field is added via [ExtendObjectType(OperationTypeNames.Query)]
// type extensions (RegistryQueries, LineageQueries), one class per surface,
// the same split this repo's other multi-concern projects already use.
public class Query;
