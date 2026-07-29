[← Libraries index](../README.md)

# Jint (dotnet)

**What it's for:** a JavaScript interpreter written entirely in C#,
embeddable in a .NET process with no native dependency — runs untrusted
or semi-trusted JS in a sandboxed `Engine` instance with configurable
resource limits (recursion depth, statement count, timeouts).

**Why bought, not built:** `ADR-037` calls for sandboxed JS execution for
the rare, genuinely-complex upcast mapping case — writing a JS
interpreter from scratch to run a small number of user-authored
transform functions safely is exactly the kind of complex, security-
sensitive task to buy rather than build.

## General usage

```csharp
var engine = new Engine(options => options
    .LimitRecursion(64)
    .TimeoutInterval(TimeSpan.FromMilliseconds(50)));

engine.SetValue("input", upcastInputJson);
var result = engine.Evaluate("function upcast(e) { e.FullName = e.First + ' ' + e.Last; return e; } upcast(input);");
```

## Where this project uses it

`ADR-018`/`ADR-037` — the complex-case half of upcast mapping (the
common, purely-declarative case uses [CEL](cel-dotnet.md) instead,
per the same ADRs); each `UpcastChain` step's JS transform runs in its
own bounded `Engine` instance, never given access to anything beyond the
one event payload it's mapping.

## Links

- [github.com/sebastienros/jint](https://github.com/sebastienros/jint)
