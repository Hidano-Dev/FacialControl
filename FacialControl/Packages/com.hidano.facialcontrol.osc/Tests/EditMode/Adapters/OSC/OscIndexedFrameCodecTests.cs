using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscIndexedFrameCodecTests : SizedTestFixture
    {
        private const int Budget = OscIndexedFrameCodec.DefaultMaxMessageBytes;
        private static readonly Guid SenderUuid = new Guid("0f8fad5b-d9cb-469f-a165-70867728950e");

        // ---- 値フレーム ----

        [Test]
        public void WriteValuesMessage_ReadByPacketReader_ReturnsVersionOffsetAndValues()
        {
            var buffer = new byte[256];
            int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, 123456, 3, new[] { 0f, 0.25f, 1f });
            byte[] packet = Slice(buffer, length);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.IsValuesAddress(view.Address), Is.True);
            Assert.That(OscIndexedFrameCodec.TryReadValuesHeader(in view, out int version, out int offset, out int count), Is.True);
            var slots = new float[6];
            Assert.That(OscIndexedFrameCodec.TryCopyValues(in view, slots, out int written), Is.True);

            Assert.That(length, Is.EqualTo(OscIndexedFrameCodec.GetValuesMessageSize(3)));
            Assert.That(version, Is.EqualTo(123456));
            Assert.That(offset, Is.EqualTo(3));
            Assert.That(count, Is.EqualTo(3));
            Assert.That(written, Is.EqualTo(3));
            Assert.That(slots, Is.EqualTo(new[] { 0f, 0f, 0f, 0f, 0.25f, 1f }));
        }

        [Test]
        public void WriteValuesMessage_NoValues_ReadsEmptyFrame()
        {
            var buffer = new byte[64];
            int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, -5, 0, ReadOnlySpan<float>.Empty);

            var reader = new OscPacketReader(Slice(buffer, length));
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.TryReadValuesHeader(in view, out int version, out _, out int count), Is.True);
            Assert.That(version, Is.EqualTo(-5));
            Assert.That(count, Is.EqualTo(0));
        }

        [Test]
        public void WriteValuesMessage_DestinationTooSmall_Throws()
        {
            var buffer = new byte[OscIndexedFrameCodec.GetValuesMessageSize(2) - 1];

            Assert.Throws<ArgumentException>(
                () => OscIndexedFrameCodec.WriteValuesMessage(buffer, 1, 0, new[] { 0f, 1f }));
        }

        [Test]
        public void GetMaxValuesPerMessage_DefaultBudget_FillsBudgetWithoutExceedingIt()
        {
            int max = OscIndexedFrameCodec.GetMaxValuesPerMessage(Budget);

            Assert.That(max, Is.GreaterThan(0));
            Assert.That(OscIndexedFrameCodec.GetValuesMessageSize(max), Is.LessThanOrEqualTo(Budget));
            Assert.That(OscIndexedFrameCodec.GetValuesMessageSize(max + 1), Is.GreaterThan(Budget));
        }

        [Test]
        public void GetMaxValuesPerMessage_BudgetBelowOneValue_ReturnsZero()
        {
            Assert.That(OscIndexedFrameCodec.GetMaxValuesPerMessage(OscIndexedFrameCodec.GetValuesMessageSize(1) - 1), Is.EqualTo(0));
        }

        [Test]
        public void GetValuesMessageCount_SplitsSlotsAndKeepsOneMessageForEmptyLayout()
        {
            Assert.That(OscIndexedFrameCodec.GetValuesMessageCount(0, 100), Is.EqualTo(1));
            Assert.That(OscIndexedFrameCodec.GetValuesMessageCount(100, 100), Is.EqualTo(1));
            Assert.That(OscIndexedFrameCodec.GetValuesMessageCount(101, 100), Is.EqualTo(2));
        }

        [Test]
        public void ValuesMessages_400SlotsSplitByBudget_RestoreEverySlotWithinBudget()
        {
            const int slotCount = 400;
            var source = new float[slotCount];
            for (int i = 0; i < slotCount; i++)
            {
                source[i] = i / (float)slotCount;
            }

            int perMessage = OscIndexedFrameCodec.GetMaxValuesPerMessage(Budget);
            int messageCount = OscIndexedFrameCodec.GetValuesMessageCount(slotCount, perMessage);
            var restored = new float[slotCount];
            var buffer = new byte[Budget];
            for (int m = 0; m < messageCount; m++)
            {
                int offset = m * perMessage;
                int count = Math.Min(perMessage, slotCount - offset);
                int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, 9, offset, new ReadOnlySpan<float>(source, offset, count));
                Assert.That(length, Is.LessThanOrEqualTo(Budget));

                var reader = new OscPacketReader(Slice(buffer, length));
                Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
                Assert.That(OscIndexedFrameCodec.TryCopyValues(in view, restored, out int written), Is.True);
                Assert.That(written, Is.EqualTo(count));
            }

            Assert.That(messageCount, Is.GreaterThan(1));
            Assert.That(restored, Is.EqualTo(source));
        }

        [Test]
        public void TryCopyValues_WithParsedHeader_CopiesWithoutRereadingTypeTags()
        {
            var buffer = new byte[128];
            int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, 4, 1, new[] { 0.5f, 0.75f });
            var reader = new OscPacketReader(Slice(buffer, length));
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.TryReadValuesHeader(in view, out _, out int offset, out int count), Is.True);
            var slots = new float[3];

            Assert.That(OscIndexedFrameCodec.TryCopyValues(in view, offset, count, slots), Is.True);
            Assert.That(slots, Is.EqualTo(new[] { 0f, 0.5f, 0.75f }));
            Assert.That(OscIndexedFrameCodec.TryCopyValues(in view, offset, count + 1, new float[8]), Is.False);
        }

        [Test]
        public void TryCopyValues_RangeBeyondSlots_ReturnsFalseAndWritesNothing()
        {
            var buffer = new byte[128];
            int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, 1, 2, new[] { 0.5f, 0.5f });
            var reader = new OscPacketReader(Slice(buffer, length));
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var slots = new float[3];

            Assert.That(OscIndexedFrameCodec.TryCopyValues(in view, slots, out int written), Is.False);
            Assert.That(written, Is.EqualTo(0));
            Assert.That(slots, Is.EqualTo(new[] { 0f, 0f, 0f }));
        }

        [Test]
        public void TryReadValuesHeader_NegativeOffset_ReturnsFalse()
        {
            var buffer = new byte[128];
            int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, 1, 0, new[] { 0.5f });
            byte[] packet = Slice(buffer, length);
            int argumentsStart = length - (3 * 4);
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(packet, argumentsStart + 4, 4), -1);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.TryReadValuesHeader(in view, out _, out _, out _), Is.False);
        }

        [Test]
        public void TryReadValuesHeader_LayoutMessage_ReturnsFalse()
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(new[] { "Smile" }, null);
            byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(1, 0, 1, entries, new OscFrameLayoutChunk(0, 1));

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.IsValuesAddress(view.Address), Is.False);
            Assert.That(OscIndexedFrameCodec.TryReadValuesHeader(in view, out _, out _, out _), Is.False);
        }

        // ---- 対応表 ----

        [Test]
        public void SplitLayout_400JapaneseNamesAndGaze_EveryChunkFitsBudgetAndReassemblesOutOfOrder()
        {
            var names = new string[400];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = "表情_まばたき_" + i;
            }

            var gaze = new[]
            {
                new OscFrameLayoutGazeChannel("gaze", new[] { "bone.left=Armature/Hips/Spine/Head/左目", "range=10,8,12,9" })
            };
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(names, gaze);

            OscFrameLayoutChunk[] chunks = OscIndexedFrameCodec.SplitLayout(entries, Budget);
            var packets = new List<byte[]>(chunks.Length);
            for (int c = 0; c < chunks.Length; c++)
            {
                byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(77, c, chunks.Length, entries, chunks[c]);
                Assert.That(packet.Length, Is.LessThanOrEqualTo(Budget), "chunk " + c);
                Assert.That(packet.Length, Is.EqualTo(OscIndexedFrameCodec.GetLayoutMessageSize(entries, chunks[c].Start, chunks[c].Count)));
                packets.Add(packet);
            }

            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(77);
            var received = new List<OscFrameLayoutEntry>();
            for (int c = packets.Count - 1; c >= 0; c--)
            {
                var reader = new OscPacketReader(packets[c]);
                Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
                Assert.That(OscIndexedFrameCodec.IsLayoutAddress(view.Address), Is.True);
                Assert.That(
                    OscIndexedFrameCodec.TryReadLayoutMessage(in view, out int version, out int chunkIndex, out int chunkCount, received),
                    Is.True);
                Assert.That(assembler.TryAddChunk(version, chunkIndex, chunkCount, received), Is.True);
            }

            Assert.That(chunks.Length, Is.GreaterThan(1));
            Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
            Assert.That(layout.Version, Is.EqualTo(77));
            Assert.That(layout.BlendShapeNames, Is.EqualTo(names));
            Assert.That(layout.GazeChannels.Count, Is.EqualTo(1));
            Assert.That(layout.GazeChannels[0].Id, Is.EqualTo("gaze"));
            Assert.That(layout.GazeChannels[0].Attributes, Is.EqualTo(gaze[0].Attributes));
            Assert.That(layout.SlotCount, Is.EqualTo(402));
        }

        [Test]
        public void SplitLayout_ChunksCoverEveryEntryInOrder()
        {
            var names = new string[300];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = "Shape" + i;
            }

            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(names, null);
            OscFrameLayoutChunk[] chunks = OscIndexedFrameCodec.SplitLayout(entries, Budget);

            int expectedStart = 0;
            for (int c = 0; c < chunks.Length; c++)
            {
                Assert.That(chunks[c].Start, Is.EqualTo(expectedStart));
                Assert.That(chunks[c].Count, Is.GreaterThan(0));
                expectedStart += chunks[c].Count;
            }

            Assert.That(expectedStart, Is.EqualTo(entries.Length));
        }

        [Test]
        public void SplitLayout_NoEntries_ReturnsOneEmptyChunkThatBuildsEmptyLayout()
        {
            var entries = Array.Empty<OscFrameLayoutEntry>();
            OscFrameLayoutChunk[] chunks = OscIndexedFrameCodec.SplitLayout(entries, Budget);
            Assert.That(chunks.Length, Is.EqualTo(1));
            Assert.That(chunks[0].Count, Is.EqualTo(0));

            byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(5, 0, 1, entries, chunks[0]);
            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var received = new List<OscFrameLayoutEntry>();
            Assert.That(
                OscIndexedFrameCodec.TryReadLayoutMessage(in view, out int version, out int chunkIndex, out int chunkCount, received),
                Is.True);

            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(version);
            Assert.That(assembler.TryAddChunk(version, chunkIndex, chunkCount, received), Is.True);
            Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
            Assert.That(layout.SlotCount, Is.EqualTo(0));
        }

        [Test]
        public void SplitLayout_EntryLargerThanBudget_GetsItsOwnChunk()
        {
            var entries = new[]
            {
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "A"),
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, new string('x', 200)),
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "B")
            };

            OscFrameLayoutChunk[] chunks = OscIndexedFrameCodec.SplitLayout(entries, 100);

            Assert.That(chunks.Length, Is.EqualTo(3));
            Assert.That(chunks[1].Start, Is.EqualTo(1));
            Assert.That(chunks[1].Count, Is.EqualTo(1));
        }

        [Test]
        public void WriteLayoutMessage_ChunkIndexOutOfRange_Throws()
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(new[] { "Smile" }, null);

            Assert.Throws<ArgumentOutOfRangeException>(
                () => OscIndexedFrameCodec.WriteLayoutMessage(1, 1, 1, entries, new OscFrameLayoutChunk(0, 1)));
        }

        [Test]
        public void TryReadLayoutMessage_ChunkIndexNotBelowChunkCount_ReturnsFalse()
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(new[] { "Smile" }, null);
            byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(1, 0, 1, entries, new OscFrameLayoutChunk(0, 1));
            int argumentsStart = GetLayoutArgumentsStart(1);
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(packet, argumentsStart + 4, 4), 1);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var received = new List<OscFrameLayoutEntry> { new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "stale") };
            Assert.That(OscIndexedFrameCodec.TryReadLayoutMessage(in view, out _, out _, out _, received), Is.False);
            Assert.That(received, Is.Empty);
        }

        [Test]
        public void TryReadLayoutMessage_ChunkCountAboveLimit_ReturnsFalse()
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(new[] { "Smile" }, null);
            byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(1, 0, 1, entries, new OscFrameLayoutChunk(0, 1));
            int argumentsStart = GetLayoutArgumentsStart(1);
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(packet, argumentsStart + 8, 4), int.MaxValue);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.TryReadLayoutMessage(in view, out _, out _, out _, new List<OscFrameLayoutEntry>()), Is.False);
        }

        [Test]
        public void LayoutMessage_NameWithNul_RoundTripsWithReplacementCharacter()
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(new[] { "A\0B", "C" }, null);
            OscFrameLayoutChunk[] chunks = OscIndexedFrameCodec.SplitLayout(entries, Budget);
            byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(1, 0, chunks.Length, entries, chunks[0]);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var received = new List<OscFrameLayoutEntry>();
            Assert.That(OscIndexedFrameCodec.TryReadLayoutMessage(in view, out _, out _, out _, received), Is.True);
            Assert.That(received.Count, Is.EqualTo(2));
            Assert.That(received[0].Value, Is.EqualTo("A\uFFFDB"));
            Assert.That(received[1].Value, Is.EqualTo("C"));
        }

        [Test]
        public void TryReadLayoutMessage_UndefinedEntryKind_ReturnsFalse()
        {
            OscFrameLayoutEntry[] entries = OscFrameLayout.ToEntries(new[] { "Smile" }, null);
            byte[] packet = OscIndexedFrameCodec.WriteLayoutMessage(1, 0, 1, entries, new OscFrameLayoutChunk(0, 1));
            int argumentsStart = GetLayoutArgumentsStart(1);
            BinaryPrimitives.WriteInt32BigEndian(new Span<byte>(packet, argumentsStart + 12, 4), 99);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var received = new List<OscFrameLayoutEntry>();
            Assert.That(OscIndexedFrameCodec.TryReadLayoutMessage(in view, out _, out _, out _, received), Is.False);
            Assert.That(received, Is.Empty);
        }

        // ---- 対応表要求 ----

        [Test]
        public void LayoutRequest_WithChunkIndices_RoundTrips()
        {
            byte[] packet = OscIndexedFrameCodec.WriteLayoutRequestMessage(SenderUuid, 314, new[] { 1, 4 });

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.IsLayoutRequestAddress(view.Address), Is.True);
            var indices = new List<int>();
            Assert.That(OscIndexedFrameCodec.TryReadLayoutRequestMessage(in view, out Guid uuid, out int version, indices), Is.True);

            Assert.That(uuid, Is.EqualTo(SenderUuid));
            Assert.That(version, Is.EqualTo(314));
            Assert.That(indices, Is.EqualTo(new[] { 1, 4 }));
        }

        [Test]
        public void LayoutRequest_WithoutChunkIndices_MeansAllChunks()
        {
            byte[] packet = OscIndexedFrameCodec.WriteLayoutRequestMessage(SenderUuid, 314, null);

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var indices = new List<int> { 9 };
            Assert.That(OscIndexedFrameCodec.TryReadLayoutRequestMessage(in view, out Guid uuid, out int version, indices), Is.True);

            Assert.That(uuid, Is.EqualTo(SenderUuid));
            Assert.That(version, Is.EqualTo(314));
            Assert.That(indices, Is.Empty);
        }

        [Test]
        public void LayoutRequest_NegativeChunkIndex_ReturnsFalse()
        {
            byte[] packet = OscIndexedFrameCodec.WriteLayoutRequestMessage(SenderUuid, 314, new[] { -1 });

            var reader = new OscPacketReader(packet);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            var indices = new List<int>();
            Assert.That(OscIndexedFrameCodec.TryReadLayoutRequestMessage(in view, out _, out _, indices), Is.False);
            Assert.That(indices, Is.Empty);
        }

        [Test]
        public void GetLayoutRequestMessageSize_MatchesWrittenLength()
        {
            var indices = new[] { 0, 1, 2, 3, 4 };

            byte[] packet = OscIndexedFrameCodec.WriteLayoutRequestMessage(SenderUuid, 314, indices);

            Assert.That(OscIndexedFrameCodec.GetLayoutRequestMessageSize(indices.Length), Is.EqualTo(packet.Length));
            Assert.That(OscIndexedFrameCodec.GetLayoutRequestMessageSize(0),
                Is.EqualTo(OscIndexedFrameCodec.WriteLayoutRequestMessage(SenderUuid, 314, null).Length));
        }

        [Test]
        public void GetMaxLayoutRequestChunkIndices_DefaultBudget_FitsAndOneMoreDoesNot()
        {
            int max = OscIndexedFrameCodec.GetMaxLayoutRequestChunkIndices(Budget);

            Assert.That(max, Is.GreaterThan(0));
            Assert.That(OscIndexedFrameCodec.GetLayoutRequestMessageSize(max), Is.LessThanOrEqualTo(Budget));
            Assert.That(OscIndexedFrameCodec.GetLayoutRequestMessageSize(max + 1), Is.GreaterThan(Budget));
        }

        [Test]
        public void GetMaxLayoutRequestChunkIndices_BudgetBelowEmptyRequest_ReturnsZero()
        {
            int emptySize = OscIndexedFrameCodec.GetLayoutRequestMessageSize(0);

            Assert.That(OscIndexedFrameCodec.GetMaxLayoutRequestChunkIndices(emptySize), Is.EqualTo(0));
            Assert.That(OscIndexedFrameCodec.GetMaxLayoutRequestChunkIndices(emptySize - 1), Is.EqualTo(0));
        }

        [Test]
        public void TryReadLayoutRequestMessage_ValuesMessage_ReturnsFalse()
        {
            var buffer = new byte[64];
            int length = OscIndexedFrameCodec.WriteValuesMessage(buffer, 1, 0, new[] { 0.5f });

            var reader = new OscPacketReader(Slice(buffer, length));
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.IsLayoutRequestAddress(view.Address), Is.False);
            Assert.That(OscIndexedFrameCodec.TryReadLayoutRequestMessage(in view, out _, out _, new List<int>()), Is.False);
        }

        private static byte[] Slice(byte[] buffer, int length)
        {
            var packet = new byte[length];
            Array.Copy(buffer, packet, length);
            return packet;
        }

        /// <summary>項目 <paramref name="entryCount"/> 個の対応表メッセージで、引数が始まるバイト位置。</summary>
        private static int GetLayoutArgumentsStart(int entryCount)
        {
            int addressSize = (OscControlAddresses.Layout.Length + 4) & ~3;
            int typeTagSize = (1 + 3 + (entryCount * 2) + 4) & ~3;
            return addressSize + typeTagSize;
        }
    }
}
