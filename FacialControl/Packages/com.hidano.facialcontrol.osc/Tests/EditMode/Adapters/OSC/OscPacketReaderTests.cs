using System;
using System.Collections.Generic;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.OSC;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscPacketReaderTests : SizedTestFixture
    {
        [Test]
        public void TryReadNext_ReadsJapaneseAddressAndSequentialBlobStringArguments()
        {
            var packet = Message("/表情", ",bs", Blob(1, 2, 3), String("hello"));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.Address.SequenceEqual(Utf8("/表情")), Is.True);
            Assert.That(message.TypeTags.SequenceEqual(new byte[] { (byte)'b', (byte)'s' }), Is.True);

            var arguments = message.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out var blob), Is.True);
            Assert.That(blob.IsBlob, Is.True);
            Assert.That(blob.Bytes.SequenceEqual(new byte[] { 1, 2, 3 }), Is.True);
            Assert.That(arguments.TryReadNext(out var text), Is.True);
            Assert.That(text.IsString, Is.True);
            Assert.That(text.Bytes.SequenceEqual(Utf8("hello")), Is.True);
            Assert.That(arguments.TryReadNext(out _), Is.False);
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryReadNext_ReadsFloatIntAndPayloadlessBoolean()
        {
            var packet = Message("/value", ",fiT", Float(0.5f), Int(7));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.TryGetFirstAsFloat(out var first), Is.True);
            Assert.That(first, Is.EqualTo(0.5f));
            var arguments = message.GetArgumentReader();
            Assert.That(arguments.TryReadNext(out var floatArgument), Is.True);
            Assert.That(floatArgument.TryGetFloat(out var floatValue), Is.True);
            Assert.That(floatValue, Is.EqualTo(0.5f));
            Assert.That(arguments.TryReadNext(out var intArgument), Is.True);
            Assert.That(intArgument.TryGetInt32(out var intValue), Is.True);
            Assert.That(intValue, Is.EqualTo(7));
            Assert.That(arguments.TryReadNext(out var boolean), Is.True);
            Assert.That(boolean.Tag, Is.EqualTo((byte)'T'));
            Assert.That(boolean.Bytes.Length, Is.EqualTo(0));
        }

        [Test]
        public void TryReadNext_RejectsMalformedMessageWithoutThrowing()
        {
            var packet = new byte[] { (byte)'/', 0, 0, 0, (byte)',', (byte)'f', 0, 0, 0, 1 };
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out _), Is.False);
            Assert.That(reader.SkippedElementCount, Is.EqualTo(1));
            Assert.That(reader.LastError, Is.EqualTo(OscPacketError.Truncated));
        }

        [Test]
        public void TryReadNext_SkipsMalformedElementsAndContinuesInArrivalOrder()
        {
            var packet = Bundle(9UL,
                new byte[] { (byte)'/', 0, 0, 0 },
                Message("", ",f", Float(1f)),
                Message("/bad-tags", "f", Float(1f)),
                Message("/missing-argument", ",f"),
                Message("/unknown-tag", ",z"),
                Message("/valid", ",f", Float(0.75f)));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.Address.SequenceEqual(Utf8("/valid")), Is.True);
            Assert.That(reader.TryReadNext(out _), Is.False);
            Assert.That(reader.SkippedElementCount, Is.EqualTo(5));
            Assert.That(reader.LastError, Is.EqualTo(OscPacketError.UnknownTypeTag));
        }

        [Test]
        public void TryReadNext_SkipsMisalignedElementAndContinues()
        {
            var packet = BundleWithRawElements(10UL,
                new byte[] { 1, 2 },
                Message("/after-misaligned", ",i", Int(3)));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.Address.SequenceEqual(Utf8("/after-misaligned")), Is.True);
            Assert.That(reader.SkippedElementCount, Is.EqualTo(1));
            Assert.That(reader.LastError, Is.EqualTo(OscPacketError.Misaligned));
        }

        [Test]
        public void TryReadNext_SkipsOverdeepNestedBundleAndContinues()
        {
            var deep = Bundle(12UL, Message("/too-deep", ",T"));
            for (var i = 0; i < 7; i++)
            {
                deep = Bundle(12UL, deep);
            }

            var packet = BundleWithRawElements(11UL, deep, Message("/after-depth", ",T"));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var message), Is.True);
            Assert.That(message.Address.SequenceEqual(Utf8("/after-depth")), Is.True);
            Assert.That(reader.TryReadNext(out _), Is.False);
            Assert.That(reader.SkippedElementCount, Is.EqualTo(1));
            Assert.That(reader.LastError, Is.EqualTo(OscPacketError.BundleTooDeep));
        }

        [Test]
        public void TryReadNext_DoesNotAllocateWhileScanning()
        {
            var packet = Message("/value", ",f", Float(0.25f));
            bool readOk = true;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                var reader = new OscPacketReader(packet);
                if (!reader.TryReadNext(out var message)) readOk = false;
                if (!message.TryGetFirstAsFloat(out _)) readOk = false;
            }, 100);

            Assert.That(readOk, Is.True);
            Assert.That(allocated, Is.EqualTo(0));
        }

        [Test]
        public void TryReadNext_EnumeratesBundleMessagesInArrivalOrderWithTimestamp()
        {
            var packet = Bundle(0x0000000200000000UL,
                Message("/first", ",f", Float(0.25f)),
                Message("/second", ",i", Int(7)));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var first), Is.True);
            Assert.That(first.Address.SequenceEqual(Utf8("/first")), Is.True);
            Assert.That(first.TimestampKey, Is.EqualTo(0x0000000200000000UL));
            Assert.That(reader.TryReadNext(out var second), Is.True);
            Assert.That(second.Address.SequenceEqual(Utf8("/second")), Is.True);
            Assert.That(second.TimestampKey, Is.EqualTo(0x0000000200000000UL));
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryReadNext_PropagatesNestedBundleTimestampDepthFirst()
        {
            var packet = Bundle(3UL,
                Message("/outer", ",T"),
                Bundle(4UL, Message("/inner", ",f", Float(1f))));
            var reader = new OscPacketReader(packet);

            Assert.That(reader.TryReadNext(out var outer), Is.True);
            Assert.That(outer.TimestampKey, Is.EqualTo(3UL));
            Assert.That(reader.TryReadNext(out var inner), Is.True);
            Assert.That(inner.TimestampKey, Is.EqualTo(4UL));
            Assert.That(inner.Address.SequenceEqual(Utf8("/inner")), Is.True);
            Assert.That(reader.TryReadNext(out _), Is.False);
        }

        [Test]
        public void TryReadNext_EmptyBundleReturnsNoMessagesWithoutAllocating()
        {
            var packet = Bundle(5UL);
            bool anyMessage = false;
            int skipped = -1;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                var reader = new OscPacketReader(packet);
                if (reader.TryReadNext(out _)) anyMessage = true;
                skipped = reader.SkippedElementCount;
            }, 100);

            Assert.That(anyMessage, Is.False);
            Assert.That(allocated, Is.EqualTo(0));
            Assert.That(skipped, Is.EqualTo(0));
        }

        [Test]
        public void TryReadNext_ReadsAllMessagesFromMtuSplitBuilderPacketsWithSharedTimestamp()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000800000000UL;
            var messages = new OscEncodedFloat[1000];
            for (var i = 0; i < messages.Length; i++)
            {
                messages[i] = new OscEncodedFloat(Utf8("/avatar/parameters/Value" + i), i / 1000f);
            }

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("OscBundleBuilder.*MTU.*split"));
            var packetCount = builder.BuildFloatBundle(timestamp, messages);
            var messageCount = 0;
            for (var packetIndex = 0; packetIndex < packetCount; packetIndex++)
            {
                var reader = new OscPacketReader(builder.GetPacketSpan(packetIndex));
                while (reader.TryReadNext(out var message))
                {
                    Assert.That(message.TimestampKey, Is.EqualTo(timestamp));
                    messageCount++;
                }
            }

            Assert.That(messageCount, Is.EqualTo(messages.Length));
        }

        private static byte[] Message(string address, string tags, params byte[][] arguments)
        {
            var bytes = new List<byte>();
            AddPaddedString(bytes, address);
            AddPaddedString(bytes, tags);
            foreach (var argument in arguments)
            {
                bytes.AddRange(argument);
            }

            return bytes.ToArray();
        }

        private static byte[] Bundle(ulong timestamp, params byte[][] elements)
        {
            var bytes = new List<byte>(16);
            bytes.AddRange(Utf8("#bundle"));
            bytes.Add(0);
            AddLong(bytes, timestamp);
            foreach (var element in elements)
            {
                AddInt(bytes, element.Length);
                bytes.AddRange(element);
            }

            return bytes.ToArray();
        }

        private static byte[] BundleWithRawElements(ulong timestamp, params byte[][] elements)
        {
            var bytes = new List<byte>(16);
            bytes.AddRange(Utf8("#bundle"));
            bytes.Add(0);
            AddLong(bytes, timestamp);
            foreach (var element in elements)
            {
                AddInt(bytes, element.Length);
                bytes.AddRange(element);
            }

            return bytes.ToArray();
        }

        private static byte[] Blob(params byte[] value)
        {
            var result = new List<byte>();
            AddInt(result, value.Length);
            result.AddRange(value);
            while ((result.Count & 3) != 0) result.Add(0);
            return result.ToArray();
        }

        private static byte[] String(string value)
        {
            var result = new List<byte>();
            AddPaddedString(result, value);
            return result.ToArray();
        }

        private static byte[] Float(float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            return new[] { (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits };
        }

        private static byte[] Int(int value)
        {
            return new[] { (byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value };
        }

        private static void AddPaddedString(List<byte> bytes, string value)
        {
            bytes.AddRange(Utf8(value));
            bytes.Add(0);
            while ((bytes.Count & 3) != 0) bytes.Add(0);
        }

        private static byte[] Utf8(string value)
        {
            return System.Text.Encoding.UTF8.GetBytes(value);
        }

        private static void AddInt(List<byte> bytes, int value)
        {
            bytes.AddRange(Int(value));
        }

        private static void AddLong(List<byte> bytes, ulong value)
        {
            bytes.Add((byte)(value >> 56));
            bytes.Add((byte)(value >> 48));
            bytes.Add((byte)(value >> 40));
            bytes.Add((byte)(value >> 32));
            bytes.Add((byte)(value >> 24));
            bytes.Add((byte)(value >> 16));
            bytes.Add((byte)(value >> 8));
            bytes.Add((byte)value);
        }
    }
}
