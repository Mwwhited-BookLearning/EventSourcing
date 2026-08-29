using System.Globalization;
using System.Security.Cryptography;
using EventStore.Domain.SchemaRegistry;
using EventStore.Erasure;

namespace EventStore.UnitTests;

// ADR-097 -- correctness tests for the from-scratch ORE construction (see
// OrderRevealingEncryption's own header comment on why this is NOT a
// verified implementation of the published CLWW/Lewi-Wu papers). These
// tests only check the one property the mechanism must have to be useful
// at all -- Compare(Encrypt(a), Encrypt(b)) agrees with sign(a-b) for
// values encrypted under the SAME key -- not anything about its leakage
// profile, which is exactly the separate, dedicated security review
// docs/08-build-plan.md's own item for this ADR requires before Done.
[TestClass]
public class OrderRevealingEncryptionTests
{
    [TestMethod]
    public void ComparingCiphertextsOfNumbersUnderTheSameKeyAgreesWithPlaintextOrder()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var values = new[] { -1000.5, -1, 0, 0.001, 1, 42, 42.0001, 100, 999999.99 };

        foreach (var a in values)
        foreach (var b in values)
        {
            var ca = OrderRevealingEncryption.Encrypt(key, a.ToString(CultureInfo.InvariantCulture), FilterableFieldType.Number);
            var cb = OrderRevealingEncryption.Encrypt(key, b.ToString(CultureInfo.InvariantCulture), FilterableFieldType.Number);
            var expected = Math.Sign(a.CompareTo(b));
            var actual = Math.Sign(OrderRevealingEncryption.Compare(ca, cb));
            Assert.AreEqual(expected, actual, $"Compare(Encrypt({a}), Encrypt({b})) should agree with sign({a}-{b})");
        }
    }

    [TestMethod]
    public void ComparingCiphertextsOfDatesUnderTheSameKeyAgreesWithChronologicalOrder()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var baseline = DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture);
        var dates = Enumerable.Range(0, 20).Select(i => baseline.AddDays(i * 37).AddHours(i)).ToArray();

        foreach (var a in dates)
        foreach (var b in dates)
        {
            var ca = OrderRevealingEncryption.Encrypt(key, a.ToString("O", CultureInfo.InvariantCulture), FilterableFieldType.DateTimeOffset);
            var cb = OrderRevealingEncryption.Encrypt(key, b.ToString("O", CultureInfo.InvariantCulture), FilterableFieldType.DateTimeOffset);
            var expected = Math.Sign(a.CompareTo(b));
            var actual = Math.Sign(OrderRevealingEncryption.Compare(ca, cb));
            Assert.AreEqual(expected, actual, $"Compare(Encrypt({a:O}), Encrypt({b:O})) should agree with sign({a:O}-{b:O})");
        }
    }

    [TestMethod]
    public void EncryptingTheSameValueTwiceUnderTheSameKeyProducesIdenticalCiphertext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var c1 = OrderRevealingEncryption.Encrypt(key, "42.5", FilterableFieldType.Number);
        var c2 = OrderRevealingEncryption.Encrypt(key, "42.5", FilterableFieldType.Number);
        CollectionAssert.AreEqual(c1, c2);
        Assert.AreEqual(0, OrderRevealingEncryption.Compare(c1, c2));
    }

    [TestMethod]
    public void DifferentKeysProduceDifferentCiphertextForTheSameValue()
    {
        var keyA = RandomNumberGenerator.GetBytes(32);
        var keyB = RandomNumberGenerator.GetBytes(32);
        var cA = OrderRevealingEncryption.Encrypt(keyA, "42.5", FilterableFieldType.Number);
        var cB = OrderRevealingEncryption.Encrypt(keyB, "42.5", FilterableFieldType.Number);
        CollectionAssert.AreNotEqual(cA, cB);
    }
}
