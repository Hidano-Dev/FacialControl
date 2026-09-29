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
        public void BuildFrameBundle_WithHeartbeat_WritesSenderIdentityFloatsThenHeartbeat()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000700000000UL;
            byte[] senderUuid = Guid.NewGuid().ToByteArray();
            byte[][] addresses =
            {
                Utf8("/avatar/parameters/Joy"),
                Utf8("/avatar/parameters/Angry")
            };
            float[] values = { 0.25f, 0.75f };
            string[] names = { "Joy", "Angry" };

            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                senderUuid,
                "123456789",
                addresses,
                values,
                values.Length,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(4, parsed.Count);
            Assert.AreEqual(SenderIdentity.OscAddress, parsed[0].address);
            AssertFloatMessage(parsed[1], "/avatar/parameters/Joy", 0.25f, timestamp);
            AssertFloatMessage(parsed[2], "/avatar/parameters/Angry", 0.75f, timestamp);
            Assert.AreEqual("/_facialcontrol/blendshape_names", parsed[3].address);
            Assert.AreEqual(timestamp, parsed[3].timestamp.value);
            CollectionAssert.AreEqual(names, parsed[3].values);
        }

        [Test]
        public void BuildFrameBundle_WithHeartbeatAndPreset_WritesIndependentPresetMessage()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000900000000UL;
            byte[] senderUuid = Guid.NewGuid().ToByteArray();
            byte[][] addresses =
            {
                Utf8("/avatar/parameters/Joy")
            };
            float[] values = { 0.25f };
            string[] names = { "Joy" };

            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                senderUuid,
                "123456789",
                addresses,
                values,
                values.Length,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length,
                Utf8("/_facialcontrol/preset"),
                "vrchat",
                customPrefix: null);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(4, parsed.Count);
            AssertFloatMessage(parsed[1], "/avatar/parameters/Joy", 0.25f, timestamp);
            Assert.AreEqual("/_facialcontrol/blendshape_names", parsed[2].address);
            CollectionAssert.AreEqual(names, parsed[2].values);
            Assert.AreEqual("/_facialcontrol/preset", parsed[3].address);
            Assert.AreEqual(timestamp, parsed[3].timestamp.value);
            CollectionAssert.AreEqual(new object[] { "vrchat" }, parsed[3].values);
        }

        [Test]
        public void BuildPresetBundle_CustomPreset_WritesPresetAndCustomPrefix()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000A00000000UL;

            int packetCount = builder.BuildPresetBundle(
                timestamp,
                Utf8("/_facialcontrol/preset"),
                "custom",
                "/myapp/");

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("/_facialcontrol/preset", parsed[0].address);
            Assert.AreEqual(timestamp, parsed[0].timestamp.value);
            CollectionAssert.AreEqual(new object[] { "custom", "/myapp/" }, parsed[0].values);
        }

        [Test]
        public void BuildHeartbeatBundle_WithPreset_DoesNotMutateBlendShapeNamePayload()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000B00000000UL;
            string[] names = { "Joy", "Angry" };

            int packetCount = builder.BuildHeartbeatBundle(
                timestamp,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length,
                Utf8("/_facialcontrol/preset"),
                "arkit",
                customPrefix: null);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(2, parsed.Count);
            Assert.AreEqual("/_facialcontrol/blendshape_names", parsed[0].address);
            CollectionAssert.AreEqual(names, parsed[0].values);
            Assert.AreEqual("/_facialcontrol/preset", parsed[1].address);
            CollectionAssert.AreEqual(new object[] { "arkit" }, parsed[1].values);
        }

        [Test]
        public void BuildFrameBundle_LargeHeartbeat_SplitsWithSenderIdentityAtEachPacketHead()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000800000000UL;
            byte[] senderUuid = Guid.NewGuid().ToByteArray();
            byte[][] addresses = { Utf8("/avatar/parameters/Joy") };
            float[] values = { 0.25f };
            string[] names = new string[300];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = "BlendShape_" + i.ToString("D4", CultureInfo.InvariantCulture);
            }

            LogAssert.Expect(LogType.Warning, new Regex("OscBundleBuilder.*MTU.*split"));
            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                senderUuid,
                "123456789",
                addresses,
                values,
                values.Length,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length);

            Assert.Greater(packetCount, 1);
            var parsedNames = new List<string>(names.Length);
            for (int i = 0; i < packetCount; i++)
            {
                List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(i));
                Assert.Greater(parsed.Count, 0);
                Assert.AreEqual(SenderIdentity.OscAddress, parsed[0].address);
                Assert.AreEqual(timestamp, parsed[0].timestamp.value);

                for (int messageIndex = 1; messageIndex < parsed.Count; messageIndex++)
                {
                    if (parsed[messageIndex].address != "/_facialcontrol/blendshape_names")
                    {
                        continue;
                    }

                    for (int valueIndex = 0; valueIndex < parsed[messageIndex].values.Length; valueIndex++)
                    {
                        parsedNames.Add((string)parsed[messageIndex].values[valueIndex]);
                    }
                }
            }

            CollectionAssert.AreEqual(names, parsedNames);
        }

        [Test]
        public void BuildHeartbeatBundle_StringValues_WritesParseableStringMessage()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000400000000UL;
            string[] names = { "Happy", "Angry", "Sad" };

            int packetCount = builder.BuildHeartbeatBundle(
                timestamp,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> parsed = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(1, parsed.Count);
            Assert.AreEqual("/_facialcontrol/blendshape_names", parsed[0].address);
            Assert.AreEqual(timestamp, parsed[0].timestamp.value);
            CollectionAssert.AreEqual(names, parsed[0].values);
        }

        [Test]
        public void BuildHeartbeatBundle_LargeNameList_SplitsIntoParseableMessages()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000500000000UL;
            string[] names = new string[300];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = "BlendShape_" + i.ToString("D4", CultureInfo.InvariantCulture);
            }

            LogAssert.Expect(LogType.Warning, new Regex("OscBundleBuilder.*MTU.*split"));
            int packetCount = builder.BuildHeartbeatBundle(
                timestamp,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length);

            Assert.Greater(packetCount, 1);
            var parsedNames = new List<string>(names.Length);
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                Assert.LessOrEqual(packet.Length, OscBundleBuilder.DefaultMaxPacketSize);

                List<uOSC.Message> parsed = ParseMessages(packet);
                for (int messageIndex = 0; messageIndex < parsed.Count; messageIndex++)
                {
                    Assert.AreEqual("/_facialcontrol/blendshape_names", parsed[messageIndex].address);
                    Assert.AreEqual(timestamp, parsed[messageIndex].timestamp.value);
                    for (int valueIndex = 0; valueIndex < parsed[messageIndex].values.Length; valueIndex++)
                    {
                        parsedNames.Add((string)parsed[messageIndex].values[valueIndex]);
                    }
                }
            }

            CollectionAssert.AreEqual(names, parsedNames);
        }

        [Test]
        public void BuildFrameBundle_WithGazeAdvertisement_WritesStringPairsWithFrameMetadata()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000600000000UL;
            byte[][] addresses = { Utf8("/avatar/parameters/A") };
            float[] values = { 0.25f };
            string[] names = { "JawOpen" };
            string[] gazePairs = { "look", "ARKit_8BS", "blink", "VRChat_XY" };

            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                new byte[SenderIdentity.UuidByteLength],
                "123456789",
                addresses,
                values,
                values.Length,
                Utf8("/_facialcontrol/blendshape_names"),
                names,
                names.Length,
                Utf8("/_facialcontrol/preset"),
                "vrchat",
                null,
                Utf8("/_facialcontrol/gaze"),
                gazePairs,
                2);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> messages = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(5, messages.Count);
            Assert.AreEqual(SenderIdentity.OscAddress, messages[0].address);
            Assert.AreEqual("/_facialcontrol/blendshape_names", messages[2].address);
            Assert.AreEqual("/_facialcontrol/preset", messages[3].address);
            Assert.AreEqual("/_facialcontrol/gaze", messages[4].address);
            Assert.AreEqual(timestamp, messages[4].timestamp.value);
            CollectionAssert.AreEqual(gazePairs, messages[4].values);
        }

        [Test]
        public void BuildFrameBundle_LargeGazeAdvertisement_SplitsOnlyAtPairBoundariesWithSenderIdentity()
        {
            using var builder = new OscBundleBuilder(maxPacketSize: 160);
            const ulong timestamp = 0x0000000700000000UL;
            string[] gazePairs = new string[20];
            for (int i = 0; i < gazePairs.Length; i += 2)
            {
                gazePairs[i] = "gaze_expression_" + i.ToString("D2", CultureInfo.InvariantCulture);
                gazePairs[i + 1] = "ARKit_8BS";
            }

            LogAssert.Expect(LogType.Warning, new Regex("OscBundleBuilder.*MTU.*split"));
            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                new byte[SenderIdentity.UuidByteLength],
                "123456789",
                Array.Empty<byte[]>(),
                Array.Empty<float>(),
                0,
                null,
                null,
                0,
                null,
                null,
                null,
                Utf8("/_facialcontrol/gaze"),
                gazePairs,
                gazePairs.Length / 2);

            Assert.Greater(packetCount, 1);
            int parsedPairCount = 0;
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                Assert.LessOrEqual(packet.Length, 160);
                List<uOSC.Message> messages = ParseMessages(packet);
                Assert.AreEqual(SenderIdentity.OscAddress, messages[0].address);
                for (int messageIndex = 1; messageIndex < messages.Count; messageIndex++)
                {
                    Assert.AreEqual("/_facialcontrol/gaze", messages[messageIndex].address);
                    Assert.AreEqual(0, messages[messageIndex].values.Length % 2);
                    parsedPairCount += messages[messageIndex].values.Length / 2;
                }
            }

            Assert.AreEqual(gazePairs.Length / 2, parsedPairCount);
        }

        [Test]
        public void BuildFrameBundle_WithoutGazeAdvertisement_PreservesExistingPayload()
        {
            using var builder = new OscBundleBuilder();
            const ulong timestamp = 0x0000000800000000UL;
            byte[][] addresses = { Utf8("/avatar/parameters/A") };
            float[] values = { 0.5f };

            int packetCount = builder.BuildFrameBundle(
                timestamp,
                Utf8(SenderIdentity.OscAddress),
                new byte[SenderIdentity.UuidByteLength],
                "123456789",
                addresses,
                values,
                values.Length,
                Utf8("/_facialcontrol/blendshape_names"),
                new[] { "JawOpen" },
                1);

            Assert.AreEqual(1, packetCount);
            List<uOSC.Message> messages = ParseMessages(builder.GetPacket(0));
            Assert.AreEqual(3, messages.Count);
            Assert.AreEqual("/_facialcontrol/blendshape_names", messages[2].address);
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
