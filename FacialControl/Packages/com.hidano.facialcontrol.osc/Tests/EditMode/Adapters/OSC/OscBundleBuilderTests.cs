using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    public sealed class OscBundleBuilderTests : SizedTestFixture
    {
        [Test]
        public void BuildFloatBundle_PreencodedAddresses_WritesParseableOscBundle()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000200000000UL;
            var messages = new[]
            {
                new OscEncodedFloat(Utf8("/avatar/parameters/Joy"), 0.25f),
                new OscEncodedFloat(Utf8("/avatar/parameters/Angry"), 0.75f)
            };

            int packetCount = builder.BuildFloatBundle(timestamp, messages);

            Assert.AreEqual(1, packetCount);
            OscBundlePacket packet = builder.GetPacket(0);
            Assert.LessOrEqual(packet.Length, OscBundleBuilder.DefaultMaxPacketSize);

            List<uOSC.Message> parsed = ParseMessages(packet);
            Assert.AreEqual(2, parsed.Count);
            AssertFloatMessage(parsed[0], "/avatar/parameters/Joy", 0.25f, timestamp);
            AssertFloatMessage(parsed[1], "/avatar/parameters/Angry", 0.75f, timestamp);
        }

        [Test]
        public void BuildFloatBundle_MtuExceeded_SplitsPacketsWithSameTimestamp()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000300000000UL;
            var messages = new OscEncodedFloat[1000];
            for (int i = 0; i < messages.Length; i++)
            {
                string address = "/avatar/parameters/BlendShape_" + i.ToString("D4", CultureInfo.InvariantCulture);
                messages[i] = new OscEncodedFloat(Utf8(address), i / 1000f);
            }

            LogAssert.Expect(LogType.Warning, new Regex("OscBundleBuilder.*MTU.*split"));
            int packetCount = builder.BuildFloatBundle(timestamp, messages);

            Assert.Greater(packetCount, 1);
            Assert.AreEqual(packetCount - 1, builder.ContinuationCount);

            int parsedCount = 0;
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                Assert.LessOrEqual(packet.Length, OscBundleBuilder.DefaultMaxPacketSize);
                Assert.AreEqual(timestamp, packet.Timestamp);
                Assert.Greater(packet.MessageCount, 0);

                List<uOSC.Message> parsed = ParseMessages(packet);
                parsedCount += parsed.Count;
                for (int messageIndex = 0; messageIndex < parsed.Count; messageIndex++)
                {
                    Assert.AreEqual(timestamp, parsed[messageIndex].timestamp.value);
                }
            }

            Assert.AreEqual(messages.Length, parsedCount);
        }

        [Test]
        public void BuildFrameBundle_SenderIdentityAndFloats_WritesSingleTimestampedBundle()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000600000000UL;
            byte[] senderUuid = Guid.NewGuid().ToByteArray();
            byte[][] addresses =
            {
                Utf8("/avatar/parameters/Joy"),
                Utf8("/avatar/parameters/Angry")
            };
            float[] values = { 0.25f, 0.75f };

            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                senderUuid,
                "123456789",
                addresses,
                values,
                values.Length);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(3, parsed.Count);
            Assert.AreEqual(SenderIdentity.OscAddress, parsed[0].address);
            Assert.AreEqual(timestamp, parsed[0].timestamp.value);
            Assert.AreEqual(2, parsed[0].values.Length);
            CollectionAssert.AreEqual(senderUuid, (byte[])parsed[0].values[0]);
            Assert.AreEqual("123456789", parsed[0].values[1]);
            AssertFloatMessage(parsed[1], "/avatar/parameters/Joy", 0.25f, timestamp);
            AssertFloatMessage(parsed[2], "/avatar/parameters/Angry", 0.75f, timestamp);
        }

        [Test]
        public void BuildFrameBundle_ManyFloats_SplitsWithSenderIdentityAtEachPacketHead()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000800000000UL;
            byte[] senderUuid = Guid.NewGuid().ToByteArray();
            byte[][] addresses = new byte[300][];
            float[] values = new float[addresses.Length];
            for (int i = 0; i < addresses.Length; i++)
            {
                addresses[i] = Utf8("/avatar/parameters/BlendShape_" + i.ToString("D4", CultureInfo.InvariantCulture));
                values[i] = i / 300f;
            }

            LogAssert.Expect(LogType.Warning, new Regex("OscBundleBuilder.*MTU.*split"));
            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                senderUuid,
                "123456789",
                addresses,
                values,
                values.Length);

            Assert.Greater(packetCount, 1);
            int floatIndex = 0;
            for (int i = 0; i < packetCount; i++)
            {
                List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(i));
                Assert.Greater(parsed.Count, 1);
                Assert.AreEqual(SenderIdentity.OscAddress, parsed[0].address);
                Assert.AreEqual(timestamp, parsed[0].timestamp.value);

                for (int messageIndex = 1; messageIndex < parsed.Count; messageIndex++)
                {
                    AssertFloatMessage(
                        parsed[messageIndex],
                        Encoding.UTF8.GetString(addresses[floatIndex]),
                        values[floatIndex],
                        timestamp);
                    floatIndex++;
                }
            }

            Assert.AreEqual(addresses.Length, floatIndex);
        }

        private static byte[] Utf8(string value)
        {
            return Encoding.UTF8.GetBytes(value);
        }

        private static List<uOSC.Message> ParseMessages(OscBundlePacket packet)
        {
            var data = new byte[packet.Length];
            Buffer.BlockCopy(packet.Buffer, 0, data, 0, packet.Length);

            var parser = new uOSC.Parser();
            int pos = 0;
            parser.Parse(data, ref pos, packet.Length);

            var messages = new List<uOSC.Message>();
            while (parser.messageCount > 0)
            {
                messages.Add(parser.Dequeue());
            }

            return messages;
        }

        private static void AssertFloatMessage(
            uOSC.Message message,
            string expectedAddress,
            float expectedValue,
            ulong expectedTimestamp)
        {
            Assert.AreEqual(expectedAddress, message.address);
            Assert.AreEqual(expectedTimestamp, message.timestamp.value);
            Assert.AreEqual(1, message.values.Length);
            Assert.AreEqual(expectedValue, (float)message.values[0], 0.0001f);
        }
    }
}
