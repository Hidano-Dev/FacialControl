using NUnit.Framework;
using uOSC;
using Hidano.FacialControl.Adapters.OSC;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscMessageSerializerTests : SizedTestFixture
    {
        [Test]
        public void TryWrite_AllSupportedTypes_RoundTripsThroughPacketReader()
        {
            var blob = new byte[] { 1, 2, 3, 0xff };
            var message = new Message("/values", 1.25f, 7, "hello", blob, true, false);

            var destination = new byte[OscMessageSerializer.GetRequiredLength(message)];
            Assert.That(OscMessageSerializer.TryWrite(message, destination, out var length), Is.True);

            var reader = new OscPacketReader(new System.ReadOnlySpan<byte>(destination, 0, length));
            Assert.That(reader.TryReadNext(out var view), Is.True);
            Assert.That(view.TimestampKey, Is.EqualTo(1UL));
            Assert.That(view.TypeTags.ToArray(), Is.EqualTo(new[] { (byte)'f', (byte)'i', (byte)'s', (byte)'b', (byte)'T', (byte)'F' }));

            var arguments = view.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out var floatValue), Is.True);
            Assert.That(floatValue.TryGetFloat(out var actualFloat), Is.True);
            Assert.That(actualFloat, Is.EqualTo(1.25f));
            Assert.That(arguments.TryReadNext(out var intValue), Is.True);
            Assert.That(intValue.TryGetInt32(out var actualInt), Is.True);
            Assert.That(actualInt, Is.EqualTo(7));
            Assert.That(arguments.TryReadNext(out var stringValue), Is.True);
            Assert.That(stringValue.Bytes.ToArray(), Is.EqualTo(new byte[] { (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o' }));
            Assert.That(arguments.TryReadNext(out var blobValue), Is.True);
            Assert.That(blobValue.Bytes.ToArray(), Is.EqualTo(blob));
            Assert.That(arguments.TryReadNext(out var trueValue), Is.True);
            Assert.That(trueValue.Tag, Is.EqualTo((byte)'T'));
            Assert.That(arguments.TryReadNext(out var falseValue), Is.True);
            Assert.That(falseValue.Tag, Is.EqualTo((byte)'F'));
            Assert.That(arguments.TryReadNext(out _), Is.False);
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryWrite_BundleTimestamp_RoundTripsTimestampAndMessage()
        {
            const ulong timestamp = 0x0000000200000003UL;
            var message = new Message("/bundle", 0.5f) { timestamp = new Timestamp(timestamp) };
            var destination = new byte[OscMessageSerializer.GetRequiredLength(message)];

            Assert.That(OscMessageSerializer.TryWrite(message, destination, out var length), Is.True);
            var reader = new OscPacketReader(new System.ReadOnlySpan<byte>(destination, 0, length));
            Assert.That(reader.TryReadNext(out var view), Is.True);
            Assert.That(view.TimestampKey, Is.EqualTo(timestamp));
            Assert.That(view.Address.ToArray(), Is.EqualTo(new[] { (byte)'/', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e' }));
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [TestCase(3)]
        [TestCase(7)]
        public void TryWrite_TypeTagLengthMultipleOfFour_KeepsNulTerminatorAndRoundTrips(int floatCount)
        {
            // ',' + n 個の 'f' がちょうど 4 の倍数になると NUL 終端が落ちやすい境界。
            var values = new object[floatCount];
            for (var i = 0; i < floatCount; i++)
            {
                values[i] = 0.5f + i;
            }

            var message = new Message("/multi", values);
            var destination = new byte[OscMessageSerializer.GetRequiredLength(message)];
            Assert.That(OscMessageSerializer.TryWrite(message, destination, out var length), Is.True);
            Assert.That(length, Is.EqualTo(destination.Length));

            var reader = new OscPacketReader(new System.ReadOnlySpan<byte>(destination, 0, length));
            Assert.That(reader.TryReadNext(out var view), Is.True);
            Assert.That(view.Address.ToArray(), Is.EqualTo(System.Text.Encoding.UTF8.GetBytes("/multi")));
            Assert.That(view.TypeTags.Length, Is.EqualTo(floatCount));

            var arguments = view.GetArgumentReader();
            for (var i = 0; i < floatCount; i++)
            {
                Assert.That(arguments.TryReadNext(out var argument), Is.True, $"argument {i}");
                Assert.That(argument.TryGetFloat(out var actual), Is.True);
                Assert.That(actual, Is.EqualTo(0.5f + i));
            }

            Assert.That(arguments.TryReadNext(out _), Is.False);
            Assert.That(reader.SkippedElementCount, Is.Zero);
        }

        [Test]
        public void TryWrite_Int64Value_WritesInvariantDecimalStringLikeWireSenderIdentity()
        {
            // OscSender / OscBundleBuilder は startedAtUnixMs を 10 進文字列で送る。
            // facade 経由の long も同じ表現になり、分類器の sender_id 解釈と一致する。
            var message = new Message("/_facialcontrol/sender_id", new byte[16], 1234567890123L);
            var destination = new byte[OscMessageSerializer.GetRequiredLength(message)];
            Assert.That(OscMessageSerializer.TryWrite(message, destination, out var length), Is.True);

            var reader = new OscPacketReader(new System.ReadOnlySpan<byte>(destination, 0, length));
            Assert.That(reader.TryReadNext(out var view), Is.True);
            Assert.That(view.TypeTags.ToArray(), Is.EqualTo(new[] { (byte)'b', (byte)'s' }));

            var arguments = view.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out _), Is.True);
            Assert.That(arguments.TryReadNext(out var startedAt), Is.True);
            Assert.That(startedAt.IsString, Is.True);
            Assert.That(System.Text.Encoding.UTF8.GetString(startedAt.Bytes.ToArray()), Is.EqualTo("1234567890123"));
        }

        [Test]
        public void TryWrite_InvalidOrUnsupportedMessage_ReturnsFalse()
        {
            Assert.That(OscMessageSerializer.TryWrite(new Message("", 1), new byte[32], out _), Is.False);
            Assert.That(OscMessageSerializer.TryWrite(new Message("/unsupported", 1.5d), new byte[64], out _), Is.False);
            Assert.That(OscMessageSerializer.TryWrite(new Message("/unsupported", null), new byte[64], out _), Is.False);
            Assert.That(OscMessageSerializer.TryWrite(new Message("/value", 1), new byte[1], out _), Is.False);
            Assert.That(OscMessageSerializer.GetRequiredLength(new Message("/unsupported", 1.5d)), Is.EqualTo(0));
        }
    }
}
