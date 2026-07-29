[← Libraries index](../README.md)

# AsyncAPI React component (web)

**What it's for:** renders a generated AsyncAPI document as a browsable
reference UI — channels, message schemas, bindings — the AsyncAPI
equivalent of what [Scalar](../dotnet/scalar.md) provides for OpenAPI.

**Why bought, not built:** an async-API reference UI is the same generic
rendering problem as an OpenAPI UI, just for a different spec; no reason
to hand-write one when the spec's own ecosystem already provides it.

## General usage

```jsx
import { AsyncApiComponent } from '@asyncapi/react-component'
import '@asyncapi/react-component/styles/default.min.css'

<AsyncApiComponent schema={{ url: '/asyncapi.json' }} />
```

## Where this project uses it

`ADR-025` — documenting the SSE/Follow-shaped surfaces (pre-`ADR-037`)
and, going forward, GraphQL Subscriptions' AsyncAPI binding
(`ADR-037`'s consequence noting `AsyncApiDocumentBuilder`, `ADR-002`, is
otherwise unaffected by the OData→GraphQL swap).

## Links

- [github.com/asyncapi/asyncapi-react](https://github.com/asyncapi/asyncapi-react)
