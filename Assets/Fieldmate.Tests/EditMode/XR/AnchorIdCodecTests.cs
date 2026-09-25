using System;
using Fieldmate.XR;
using NUnit.Framework;
using UnityEngine.XR.ARSubsystems;

namespace Fieldmate.Tests.EditMode.XR;

public class AnchorIdCodecTests
{
    [Test]
    public void EncodeThenDecode_RoundTrips()
    {
        var original = new SerializableGuid(0x0123456789ABCDEFUL, 0xFEDCBA9876543210UL);

        Assert.That(AnchorIdCodec.TryDecode(AnchorIdCodec.Encode(original), out var decoded), Is.True);
        Assert.That(decoded, Is.EqualTo(original));
    }

    [Test]
    public void Encode_Produces32HexCharacters()
    {
        var encoded = AnchorIdCodec.Encode(new SerializableGuid(1UL, 2UL));

        Assert.That(encoded, Has.Length.EqualTo(32).And.Match("^[0-9a-f]+$"));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("not-a-guid")]
    [TestCase("00000000000000000000000000000000")]
    public void TryDecode_RejectsMissingOrEmptyIds(string text)
    {
        Assert.That(AnchorIdCodec.TryDecode(text, out var id), Is.False);
        Assert.That(id, Is.EqualTo(SerializableGuid.empty));
    }

    [Test]
    public void TryDecode_AcceptsSurroundingWhitespace()
    {
        var guid = Guid.NewGuid();

        Assert.That(AnchorIdCodec.TryDecode($"  {guid:N}\n", out var id), Is.True);
        Assert.That(id.guid, Is.EqualTo(guid));
    }
}
