namespace EventStore.Domain.SchemaRegistry;

// "GraphQL-Only Query Layer" -- lets SchemaRegistryService announce a
// successful registration without depending on EventStore.GraphQL directly
// (the dependency runs the other way: GraphQL depends on SchemaRegistry,
// never the reverse). FollowSubscriptionTypeModule is the one real
// implementation today, hot-reloading its dynamically-built Subscription
// fields the moment a new/changed event type is registered -- the same
// "invalidate immediately on registration" discipline ADR-002's OpenAPI/
// AsyncAPI cache already established, extended to the GraphQL schema too.
public interface ISchemaChangeNotifier
{
    void NotifyChanged();
}
