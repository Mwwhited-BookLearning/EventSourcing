[← Libraries index](../README.md)

# FsCheck (dotnet)

**What it's for:** the canonical .NET property-based testing library —
instead of hand-picked example inputs, you write a *property* ("altering
any byte of any stored event breaks the hash chain") and `FsCheck`
generates hundreds of random inputs trying to falsify it, automatically
shrinking any failure to the smallest input that still reproduces it.

**Why bought, not built:** generating and shrinking random test cases
correctly is a solved, non-trivial problem (originally QuickCheck,
Haskell) with no project-specific value in reimplementing it.

## General usage

```csharp
[TestMethod]
public void AlteringAnyStoredEventBreaksTheChain()
{
    Prop.ForAll<StoredEvent[], int>((events, tamperIndex) =>
    {
        if (events.Length == 0) return true;
        var chain = BuildChain(events);
        var i = Math.Abs(tamperIndex) % events.Length;
        chain[i].Payload += "x";
        return !VerifyChain(chain);
    }).QuickCheckThrowOnFailure();
}
```

`FsCheck`'s core library is framework-agnostic — the snippet above runs
inside an ordinary MSTest `[TestMethod]` with no extra package. The
community `fscheck-mstest` package adds `[Property]`-attribute sugar, if
preferred, but isn't required.

## Where this project uses it

`ADR-063` — property-based tests for `ADR-019`'s hash-chain tamper
detection and the pure-logic half of `ADR-024`'s conflict-resolution
policy, inside `ADR-055`'s `EventStore.UnitTests`.

## Links

- [fscheck.github.io/FsCheck](https://fscheck.github.io/FsCheck/)
- [github.com/fscheck/FsCheck](https://github.com/fscheck/FsCheck)
