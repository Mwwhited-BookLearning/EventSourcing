# C4 Architecture

Diagrams use PlantUML with the C4-PlantUML macros
(`https://github.com/plantuml-stdlib/C4-PlantUML`).

## Context diagram

```plantuml
@startuml C4_Context
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Context.puml

Person(publisher, "Publishing System", "Emits domain events")
Person(follower, "Consuming System", "Subscribes to event streams")
Person(operator, "Platform Operator", "Registers event types / schemas")

System(eventStore, "Open Event Sourcing Store", "Validates, persists, and streams events. Publishes OpenAPI + AsyncAPI contracts.")

Rel(publisher, eventStore, "POST /publish/{event-type}", "HTTPS/JSON")
Rel(follower, eventStore, "GET /follow/{event-type}?$filter=...", "SSE")
Rel(operator, eventStore, "Registers/updates JSON Schemas", "HTTPS/JSON")
Rel(eventStore, publisher, "OpenAPI contract", "HTTPS")
Rel(eventStore, follower, "AsyncAPI contract", "HTTPS")

@enduml
```

## Container diagram

```plantuml
@startuml C4_Container
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Container.puml

Person(publisher, "Publishing System")
Person(follower, "Consuming System")
Person(operator, "Platform Operator")

System_Boundary(system, "Open Event Sourcing Store") {
    Container(publishApi, "Publish API", ".NET (ASP.NET Core)", "POST /publish/{event-type}; validates against registered JSON Schema; records parentEventIds")
    Container(followApi, "Follow API", ".NET (ASP.NET Core, SSE)", "GET /follow/{event-type}; parses OData $filter, streams matches")
    Container(lineageApi, "Lineage API", ".NET (ASP.NET Core)", "GET /events/{id}/parents|children|ancestors|descendants; walks the event-parent DAG")
    Container(registry, "Schema Registry Service", ".NET", "CRUD for named/versioned JSON Schemas; marks indexed/filterable fields; sets per-event-type ParentValidationMode")
    Container(specGen, "Spec Generator", ".NET", "Builds OpenAPI (publish, lineage) and AsyncAPI (follow) documents from registry state")
    ContainerDb(db, "Event & Schema Store", "EF Core over SQLite / PostgreSQL / SQL Server", "Events table, EventParents table, EventTypes/Schemas table")
}

Rel(publisher, publishApi, "Publishes events", "HTTPS/JSON")
Rel(follower, followApi, "Subscribes with $filter", "SSE")
Rel(follower, lineageApi, "Queries event lineage", "HTTPS/JSON")
Rel(operator, registry, "Registers schemas", "HTTPS/JSON")

Rel(publishApi, registry, "Fetch schema + ParentValidationMode for validation")
Rel(publishApi, db, "Append event; validate/insert EventParents rows", "EF Core")
Rel(followApi, db, "Query events (filter pushed to SQL)", "EF Core")
Rel(lineageApi, db, "Direct joins (parents/children); recursive CTE (ancestors/descendants)", "EF Core / raw SQL")
Rel(registry, db, "Persist schema metadata", "EF Core")
Rel(specGen, registry, "Read schema/event-type metadata")
Rel(publisher, specGen, "GET /openapi.json")
Rel(follower, specGen, "GET /asyncapi.json")

@enduml
```

## Component diagram — Publish API

```plantuml
@startuml C4_Component_Publish
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(publishApi, "Publish API") {
    Component(endpoint, "PublishEndpoint", "Minimal API / Controller", "Routes POST /publish/{event-type}")
    Component(validator, "SchemaValidationService", "JsonSchema.Net wrapper", "Validates payload against registered schema version")
    Component(appender, "EventAppender", "EF Core repository", "Writes StoredEvent row, assigns SequenceNumber")
    Component(registryClient, "SchemaRegistryClient", "In-process or HTTP client", "Resolves current schema for event-type")
}

ContainerDb(db, "Event & Schema Store")

Rel(endpoint, registryClient, "Get schema")
Rel(endpoint, validator, "Validate payload")
Rel(endpoint, appender, "Append on success")
Rel(registryClient, db, "Read EventTypes/Schemas")
Rel(appender, db, "Insert StoredEvent")

@enduml
```

## Component diagram — Follow API

```plantuml
@startuml C4_Component_Follow
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(followApi, "Follow API") {
    Component(sseEndpoint, "FollowEndpoint", "ASP.NET Core SSE handler", "GET /follow/{event-type}?$filter=...")
    Component(odataParser, "ODataFilterParser", "Microsoft.OData.UriParser", "Parses $filter into an OData AST")
    Component(predicateBuilder, "PredicateTranslator", "Custom", "Walks OData AST -> LINQ Expression using JsonPath functions")
    Component(jsonPathTranslator, "IJsonPathTranslator (impl per provider)", "SQLite/Postgres/SqlServer", "Maps JsonValue() calls to native SQL JSON functions")
    Component(tailReader, "EventTailReader", "EF Core repository", "Polls Events where SequenceNumber > lastSeen, applies pushed-down predicate")
}

ContainerDb(db, "Event & Schema Store")

Rel(sseEndpoint, odataParser, "Parse $filter")
Rel(odataParser, predicateBuilder, "AST")
Rel(predicateBuilder, jsonPathTranslator, "Uses registered translation")
Rel(sseEndpoint, tailReader, "Poll for new matching events")
Rel(tailReader, db, "SELECT ... WHERE json_extract/JSON_VALUE/->> (pushed down)")

@enduml
```

## Component diagram — Lineage API

```plantuml
@startuml C4_Component_Lineage
!include https://raw.githubusercontent.com/plantuml-stdlib/C4-PlantUML/master/C4_Component.puml

Container_Boundary(lineageApi, "Lineage API") {
    Component(lineageEndpoint, "LineageEndpoint", "Minimal API", "Routes GET /events/{id}/parents|children|ancestors|descendants")
    Component(directReader, "EventParentReader", "EF Core (LINQ)", "Immediate parents/children via a plain join on EventParents")
    Component(recursiveReader, "IEventLineageQueryProvider (impl per provider)", "SQLite/Postgres/SqlServer raw SQL", "Ancestors/descendants via provider-specific WITH RECURSIVE CTE")
    Component(cycleGuard, "CycleGuard", "In-process", "Bounds traversal depth / rejects a revisited node — required because Permissive-mode event types can form cycles (ADR-005)")
}

ContainerDb(db, "Event & Schema Store")

Rel(lineageEndpoint, directReader, "parents / children")
Rel(lineageEndpoint, recursiveReader, "ancestors / descendants")
Rel(recursiveReader, cycleGuard, "Guards recursion")
Rel(directReader, db, "SELECT ... FROM EventParents JOIN Events")
Rel(recursiveReader, db, "WITH RECURSIVE ... (native per provider)")

@enduml
```
