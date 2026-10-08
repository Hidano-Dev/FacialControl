using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscFrameLayoutAssemblerTests : SizedTestFixture
    {
        [Test]
        public void TryAddChunk_DifferentVersion_RejectsChunk()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);

            Assert.That(assembler.TryAddChunk(11, 0, 1, Entries("Smile")), Is.False);
            Assert.That(assembler.ReceivedChunkCount, Is.EqualTo(0));
        }

        [Test]
        public void TryAddChunk_UnknownVersion_RejectsChunk()
        {
            var assembler = new OscFrameLayoutAssembler();

            Assert.That(
                assembler.TryAddChunk(OscFrameLayoutVersion.Unknown, 0, 1, Entries("Smile")),
                Is.False);
        }

        [Test]
        public void GetMissingChunkIndices_BeforeAnyChunk_IsEmptyMeaningRequestAll()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);
            var missing = new List<int> { 3 };

            assembler.GetMissingChunkIndices(missing);

            Assert.That(missing, Is.Empty);
            Assert.That(assembler.ChunkCount, Is.EqualTo(0));
            Assert.That(assembler.IsComplete, Is.False);
        }

        [Test]
        public void TryAddChunk_OutOfOrderWithDuplicate_ReportsMissingUntilCompleteAndBuildsInChunkOrder()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);
            var missing = new List<int>();

            Assert.That(assembler.TryAddChunk(10, 2, 3, Entries("C")), Is.True);
            assembler.GetMissingChunkIndices(missing);
            Assert.That(missing, Is.EqualTo(new[] { 0, 1 }));

            Assert.That(assembler.TryAddChunk(10, 2, 3, Entries("Duplicate")), Is.True);
            Assert.That(assembler.TryAddChunk(10, 0, 3, Entries("A")), Is.True);
            assembler.GetMissingChunkIndices(missing);
            Assert.That(missing, Is.EqualTo(new[] { 1 }));
            Assert.That(assembler.TryBuild(out _), Is.False);

            Assert.That(assembler.TryAddChunk(10, 1, 3, Entries("B")), Is.True);
            assembler.GetMissingChunkIndices(missing);
            Assert.That(missing, Is.Empty);
            Assert.That(assembler.IsComplete, Is.True);
            Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
            Assert.That(layout.Version, Is.EqualTo(10));
            Assert.That(layout.BlendShapeNames, Is.EqualTo(new[] { "A", "B", "C" }));
        }

        [Test]
        public void TryAddChunk_ChunkCountDiffersWithinVersion_RejectsChunk()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);
            Assert.That(assembler.TryAddChunk(10, 0, 2, Entries("A")), Is.True);

            Assert.That(assembler.TryAddChunk(10, 1, 3, Entries("B")), Is.False);
            Assert.That(assembler.ChunkCount, Is.EqualTo(2));
            Assert.That(assembler.ReceivedChunkCount, Is.EqualTo(1));
        }

        [Test]
        public void TryAddChunk_ChunkIndexOutOfRange_RejectsChunk()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);

            Assert.That(assembler.TryAddChunk(10, 2, 2, Entries("A")), Is.False);
            Assert.That(assembler.TryAddChunk(10, -1, 2, Entries("A")), Is.False);
            Assert.That(assembler.TryAddChunk(10, 0, 0, Entries("A")), Is.False);
        }

        [Test]
        public void TryAddChunk_SourceListChangedAfterAdd_KeepsCopiedEntries()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);
            var entries = new List<OscFrameLayoutEntry>(Entries("A"));

            Assert.That(assembler.TryAddChunk(10, 0, 1, entries), Is.True);
            entries.Clear();

            Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
            Assert.That(layout.BlendShapeNames, Is.EqualTo(new[] { "A" }));
        }

        [Test]
        public void Reset_NewVersion_DiscardsReceivedChunks()
        {
            var assembler = new OscFrameLayoutAssembler();
            assembler.Reset(10);
            Assert.That(assembler.TryAddChunk(10, 0, 2, Entries("A")), Is.True);

            assembler.Reset(11);

            Assert.That(assembler.Version, Is.EqualTo(11));
            Assert.That(assembler.ChunkCount, Is.EqualTo(0));
            Assert.That(assembler.ReceivedChunkCount, Is.EqualTo(0));
            Assert.That(assembler.TryAddChunk(10, 1, 2, Entries("B")), Is.False);
        }

        private static OscFrameLayoutEntry[] Entries(params string[] blendShapeNames)
        {
            return OscFrameLayout.ToEntries(blendShapeNames, null);
        }
    }
}
