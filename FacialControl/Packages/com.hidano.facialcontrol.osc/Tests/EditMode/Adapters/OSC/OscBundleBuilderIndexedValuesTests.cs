using System;
using System.Collections.Generic;
using System.Text;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscBundleBuilderIndexedValuesTests : SizedTestFixture
    {
        private const ulong Timestamp = 0x0000000300000001UL;
        private const string StartedAt = "1760000000000";
        private const int LayoutVersion = 987654;

        private static readonly byte[] SenderIdAddress = Encoding.UTF8.GetBytes(SenderIdentity.OscAddress);
        private static readonly byte[] SenderUuid = new Guid("6f9619ff-8b86-d011-b42d-00c04fc964ff").ToByteArray();

        [Test]
        public void AppendIndexedValuesPackets_ManySlots_SplitsIntoPacketsWithSenderIdAndContiguousOffsets()
        {
            using var builder = new OscBundleBuilder();
            float[] slots = CreateSlots(800);

            int packetCount = builder.AppendIndexedValuesPackets(
                Timestamp, SenderIdAddress, SenderUuid, StartedAt, LayoutVersion, slots);

            Assert.That(packetCount, Is.GreaterThan(1));
            var received = new float[slots.Length];
            int nextOffset = 0;
            for (int i = 0; i < packetCount; i++)
            {
                OscBundlePacket packet = builder.GetPacket(i);
                Assert.That(packet.Length, Is.LessThanOrEqualTo(OscBundleBuilder.DefaultMaxPacketSize));
                Assert.That(packet.Timestamp, Is.EqualTo(Timestamp));

                List<Element> elements = ReadElements(packet);
                Assert.That(elements.Count, Is.EqualTo(2));
                Assert.That(elements[0].Address, Is.EqualTo(SenderIdentity.OscAddress));
                Assert.That(elements[1].Address, Is.EqualTo(OscControlAddresses.Values));
                Assert.That(elements[1].Version, Is.EqualTo(LayoutVersion));
                Assert.That(elements[1].Offset, Is.EqualTo(nextOffset));
                Assert.That(elements[1].MessageLength, Is.LessThanOrEqualTo(OscIndexedFrameCodec.DefaultMaxMessageBytes));
                Array.Copy(elements[1].Values, 0, received, elements[1].Offset, elements[1].Values.Length);
                nextOffset += elements[1].Values.Length;
            }

            Assert.That(nextOffset, Is.EqualTo(slots.Length));
            Assert.That(received, Is.EqualTo(slots));
        }

        [Test]
        public void AppendIndexedValuesPackets_NoSlots_SendsOneEmptyValuesFrame()
        {
            using var builder = new OscBundleBuilder();

            int packetCount = builder.AppendIndexedValuesPackets(
                Timestamp, SenderIdAddress, SenderUuid, StartedAt, LayoutVersion, ReadOnlySpan<float>.Empty);

            Assert.That(packetCount, Is.EqualTo(1));
            List<Element> elements = ReadElements(builder.GetPacket(0));
            Assert.That(elements.Count, Is.EqualTo(2));
            Assert.That(elements[1].Version, Is.EqualTo(LayoutVersion));
            Assert.That(elements[1].Values, Is.Empty);
        }

        [Test]
        public void AppendIndexedValuesPackets_AfterFrameBundle_KeepsFramePacketsAndUsesSameTimestamp()
        {
            using var builder = new OscBundleBuilder();
            var addresses = new[] { Encoding.UTF8.GetBytes("/avatar/parameters/smile") };
            int framePackets = builder.BuildFrameBundle(
                Timestamp, SenderIdAddress, SenderUuid, StartedAt, addresses, new[] { 0.5f }, 1);

            int packetCount = builder.AppendIndexedValuesPackets(
                Timestamp, SenderIdAddress, SenderUuid, StartedAt, LayoutVersion, new[] { 0.5f });

            Assert.That(packetCount, Is.EqualTo(framePackets + 1));
            List<Element> frame = ReadElements(builder.GetPacket(0));
            Assert.That(frame[frame.Count - 1].Address, Is.EqualTo("/avatar/parameters/smile"));
            OscBundlePacket valuesPacket = builder.GetPacket(framePackets);
            Assert.That(valuesPacket.Timestamp, Is.EqualTo(Timestamp));
            List<Element> values = ReadElements(valuesPacket);
            Assert.That(values[1].Values, Is.EqualTo(new[] { 0.5f }));
        }

        [Test]
        public void BuildFrameBundle_AfterAppend_StartsFromEmptyPacketList()
        {
            using var builder = new OscBundleBuilder();
            var addresses = new[] { Encoding.UTF8.GetBytes("/avatar/parameters/smile") };
            builder.AppendIndexedValuesPackets(
                Timestamp, SenderIdAddress, SenderUuid, StartedAt, LayoutVersion, CreateSlots(800));

            int packetCount = builder.BuildFrameBundle(
                Timestamp, SenderIdAddress, SenderUuid, StartedAt, addresses, new[] { 0.5f }, 1);

            Assert.That(packetCount, Is.EqualTo(1));
        }

        private static float[] CreateSlots(int count)
        {
            var slots = new float[count];
            for (int i = 0; i < count; i++)
            {
                slots[i] = i / 1000f;
            }

            return slots;
        }

        private static List<Element> ReadElements(OscBundlePacket packet)
        {
            var elements = new List<Element>();
            var reader = new OscPacketReader(packet.Span);
            while (reader.TryReadNext(out OscMessageView view))
            {
                var element = new Element { Address = Encoding.UTF8.GetString(view.Address) };
                if (OscIndexedFrameCodec.IsValuesAddress(view.Address))
                {
                    Assert.That(
                        OscIndexedFrameCodec.TryReadValuesHeader(in view, out int version, out int offset, out int count),
                        Is.True);
                    var slots = new float[offset + count];
                    Assert.That(OscIndexedFrameCodec.TryCopyValues(in view, offset, count, slots), Is.True);
                    element.Version = version;
                    element.Offset = offset;
                    element.Values = new float[count];
                    Array.Copy(slots, offset, element.Values, 0, count);
                    element.MessageLength = view.Element.Length;
                }

                elements.Add(element);
            }

            return elements;
        }

        private sealed class Element
        {
            public string Address;
            public int Version;
            public int Offset;
            public float[] Values = Array.Empty<float>();
            public int MessageLength;
        }
    }
}
