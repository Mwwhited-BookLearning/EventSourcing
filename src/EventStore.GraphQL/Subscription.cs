using HotChocolate;
using HotChocolate.Types;

namespace EventStore.GraphQL;

// FollowSubscriptionTypeModule adds every REAL field dynamically (ITypeModule,
// hot-reloaded off the schema registry), never a static [ExtendObjectType]
// extension like Query/Mutation's own surfaces, since a Subscription field's
// shape genuinely differs per registered event type. This class exists only
// to keep the root Subscription type structurally non-empty at schema-build
// time -- HotChocolate rejects an object type with zero fields, which a
// brand-new deployment with no registered event types yet would otherwise
// be (found only by actually running this: SchemaException, "the object
// type Subscription has to at least define one field," at Host startup,
// not caught by any compile-time check). OnHeartbeat never actually yields.
public class Subscription
{
    [Subscribe(With = nameof(SubscribeToHeartbeat))]
    public bool OnHeartbeat([EventMessage] bool message) => message;

    public async IAsyncEnumerable<bool> SubscribeToHeartbeat()
    {
        await Task.CompletedTask;
        yield break;
    }
}
