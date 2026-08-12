using EventStore.Domain.EventLog;
using FsCheck;
using FsCheck.Fluent;

namespace EventStore.UnitTests;

// ADR-063 -- FsCheck property-based testing for ADR-019's hash-chain
// tamper-detection claim: "ChainHash[n] = SHA-256(ChainHash[n-1] ||
// PayloadHash[n] || SequenceNumber[n])... any tamper to a stored event's
// own PayloadHash makes every SUBSEQUENT ChainHash in the log fail to
// recompute to its stored value." Rather than asserting this for one
// hand-picked example (the kind of test a chain-verification service's
// own integration tests already exercise, EventStore.IntegrationTests),
// FsCheck generates many independent chains/tamper points and checks the
// property holds for all of them -- the "cheapest, highest-confidence
// win" ADR-063's own Decision names this for.
[TestClass]
public class HashChainTamperDetectionTests
{
    [TestMethod]
    public void TamperingWithAnyLinksPayloadHashDivergesTheChainFromThatPointForward()
    {
        Prop.ForAll(
            Arb.From(Gen.Choose(2, 15)),
            chainLength => CheckTamperAlwaysDetected(chainLength))
            .QuickCheckThrowOnFailure();
    }

    private static bool CheckTamperAlwaysDetected(int chainLength)
    {
        // A deterministic, distinct payload hash per position -- FsCheck's
        // own job here is to vary chainLength and tamperIndex across many
        // runs, not to also fuzz the hash VALUES themselves (SHA-256's own
        // collision resistance is out of scope; this property is about
        // the CHAINING formula, not the hash function it's built from).
        var payloadHashes = Enumerable.Range(0, chainLength).Select(i => $"payload-hash-{i}").ToArray();

        var genuine = BuildChain(payloadHashes);

        for (var tamperIndex = 0; tamperIndex < chainLength; tamperIndex++)
        {
            var tampered = (string[])payloadHashes.Clone();
            tampered[tamperIndex] = $"TAMPERED-{tampered[tamperIndex]}";
            var recomputed = BuildChain(tampered);

            // Every ChainHash from tamperIndex onward must differ from the
            // genuine chain's own value at that same position -- this is
            // the actual tamper-evidence property, not merely "the final
            // hash differs" (which alone wouldn't rule out two DIFFERENT
            // tampers accidentally cancelling out at the very last link).
            for (var position = tamperIndex; position < chainLength; position++)
            {
                if (recomputed[position] == genuine[position])
                    return false;
            }

            // Every ChainHash BEFORE the tamper point must be COMPLETELY
            // unaffected -- tampering with event N must never retroactively
            // change the recorded hash of an earlier, untouched event.
            for (var position = 0; position < tamperIndex; position++)
            {
                if (recomputed[position] != genuine[position])
                    return false;
            }
        }

        return true;
    }

    private static string[] BuildChain(string[] payloadHashes)
    {
        var chain = new string[payloadHashes.Length];
        var priorChainHash = EventChainHash.Genesis;
        for (var i = 0; i < payloadHashes.Length; i++)
        {
            var sequenceNumber = i + 1L;
            chain[i] = EventChainHash.Compute(priorChainHash, payloadHashes[i], sequenceNumber);
            priorChainHash = chain[i];
        }
        return chain;
    }
}
