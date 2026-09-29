using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Adapters.OSC.Tests
{
    [SmallTest]
    public sealed class OscMessageClassifierTests : SizedTestFixture
    {
        [Test]
        public void TryClassify_KnownMapping_ProducesResolvedRecordWithoutAllocating()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool)
                .SetMappings(new[] { new OscMapping("/known", "known", "layer") })
                .Build(7);
            byte[] argument = { 0x3f, 0x00, 0x00, 0x00 };
            var view = new OscMessageView(Utf8("/known"), Utf8("f"), argument, argument, 23UL, 11);

            Assert.That(OscMessageClassifier.TryClassify(view, table, out var record), Is.True);
            Assert.That(record.Kind, Is.EqualTo(OscResolvedKind.BlendShape));
            Assert.That(record.HasFloat, Is.True);
            Assert.That(record.FloatValue, Is.EqualTo(0.5f));
            Assert.That(record.MappingIndex, Is.EqualTo(0));
            Assert.That(record.TimestampKey, Is.EqualTo(23UL));
            Assert.That(record.TableVersion, Is.EqualTo(7));
            Assert.That(record.ElementOffset, Is.EqualTo(11));
        }

        [Test]
        public void TryClassify_SenderIdentityBlobAndInt_ProducesIdentityRecord()
        {
            var pool = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            var table = new OscAddressKeyTable.Builder(pool).Build(3);
            Guid expected = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
            byte[] address = Utf8(OscControlAddresses.SenderId);
            byte[] tags = Utf8("bs");
            byte[] args = new byte[28];
            BinaryPrimitives.WriteInt32BigEndian(args, 16);
            expected.ToByteArray().CopyTo(args, 4);
            args[20] = (byte)'1';
            args[21] = (byte)'0';
            args[22] = (byte)'0';
            args[23] = (byte)'0';
            var view = new OscMessageView(address, tags, args, args, 1UL);

            Assert.That(OscMessageClassifier.TryClassify(view, table, out var record), Is.True);
            Assert.That(record.Control, Is.EqualTo(OscControlKind.SenderId));
            Assert.That(record.SenderIdentityValid, Is.True);
            Assert.That(record.SenderUuid, Is.EqualTo(expected));
            Assert.That(record.SenderStartedAtUnixMs, Is.EqualTo(1000L));
        }

        [Test]
        public void TryClassify_UnmappedAddress_DoesNotProduceRecord()
        {
            var table = new OscAddressKeyTable.Builder(new Dictionary<string, byte[]>()).Build(1);
            var view = new OscMessageView(Utf8("/unknown"), Utf8("T"), Array.Empty<byte>(), Array.Empty<byte>(), 0UL);
            Assert.That(OscMessageClassifier.TryClassify(view, table, out _), Is.False);
        }

        private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
    }
}
