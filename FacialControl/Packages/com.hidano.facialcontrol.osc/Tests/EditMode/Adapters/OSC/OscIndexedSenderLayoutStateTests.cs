using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscIndexedSenderLayoutStateTests : SizedTestFixture
    {
        private const double Retry = 0.5d;
        private const double WarnAfter = 3d;
        private static readonly Guid SenderUuid = new Guid("6f9619ff-8b86-d011-b42d-00cf4fc964ff");
        private static readonly object Destination = new object();

        [Test]
        public void TryCreateRequest_UnknownVersionWithDestination_RequestsAllChunksForThatVersion()
        {
            var state = CreateState();
            state.RequestDestination = Destination;

            state.ObserveValues(77, 0d);

            Assert.That(state.TryCreateRequest(0d, out byte[] request), Is.True);
            ReadRequest(request, out Guid uuid, out int version, out List<int> indices);
            Assert.That(uuid, Is.EqualTo(SenderUuid));
            Assert.That(version, Is.EqualTo(77));
            Assert.That(indices, Is.Empty, "チャンク総数が分からない間は全チャンクを要求する");
        }

        [Test]
        public void TryCreateRequest_NoDestination_DoesNotConsumeRetryInterval()
        {
            var state = CreateState();
            state.ObserveValues(77, 0d);

            Assert.That(state.TryCreateRequest(0d, out _), Is.False);

            state.RequestDestination = Destination;
            Assert.That(state.TryCreateRequest(0.01d, out _), Is.True, "宛先が分かった時点ですぐ要求する");
        }

        [Test]
        public void TryCreateRequest_SomeChunksArrived_RequestsOnlyMissingChunks()
        {
            var state = CreateState();
            state.RequestDestination = Destination;
            state.ObserveValues(77, 0d);
            Assert.That(state.TryCreateRequest(0d, out _), Is.True);

            Assert.That(state.TryAddChunk(77, 1, 3, Entries("b")), Is.True);

            Assert.That(state.TryCreateRequest(0.2d, out _), Is.False, "再要求は一定間隔を空ける");
            Assert.That(state.TryCreateRequest(Retry, out byte[] request), Is.True);
            ReadRequest(request, out _, out int version, out List<int> indices);
            Assert.That(version, Is.EqualTo(77));
            Assert.That(indices, Is.EqualTo(new[] { 0, 2 }));
        }

        [Test]
        public void TryCreateRequest_TooManyMissingChunks_RequestsAllChunksWithinBudget()
        {
            // 要求 1 通に番号を 1 つしか入れられない予算にする。
            int budget = OscIndexedFrameCodec.GetLayoutRequestMessageSize(1);
            var state = new OscIndexedSenderLayoutState(SenderUuid, Retry, WarnAfter, budget)
            {
                RequestDestination = Destination
            };
            state.ObserveValues(77, 0d);
            Assert.That(state.TryAddChunk(77, 0, 3, Entries("a")), Is.True);

            Assert.That(state.TryCreateRequest(0d, out byte[] request), Is.True);

            Assert.That(request.Length, Is.LessThanOrEqualTo(budget));
            ReadRequest(request, out _, out _, out List<int> indices);
            Assert.That(indices, Is.Empty, "欠けた番号が 1 通に収まらなければ全チャンクを要求する");
        }

        [Test]
        public void TryCreateRequest_AllChunksArrivedButNotApplied_DoesNotRequestAgain()
        {
            var state = CreateState();
            state.RequestDestination = Destination;
            state.ObserveValues(77, 0d);
            Assert.That(state.TryCreateRequest(0d, out _), Is.True);
            state.TryAddChunk(77, 0, 1, Entries("a"));

            Assert.That(state.TryCreateRequest(Retry * 2, out _), Is.False, "揃った対応表を全チャンク要求で取り直さない");

            state.DiscardAssembledChunks();
            Assert.That(state.TryCreateRequest(Retry * 2, out byte[] request), Is.True, "捨てた後は取り直す");
            ReadRequest(request, out _, out _, out List<int> indices);
            Assert.That(indices, Is.Empty);
        }

        [Test]
        public void TryAddChunk_VersionNotPending_IsRejected()
        {
            var state = CreateState();
            state.ObserveValues(77, 0d);

            Assert.That(state.TryAddChunk(78, 0, 1, Entries("a")), Is.False);
            Assert.That(state.TryTakeCompletedLayout(out _), Is.False);
        }

        [Test]
        public void TryTakeCompletedLayout_AllChunksArrivedOutOfOrder_BuildsLayoutInChunkOrder()
        {
            var state = CreateState();
            state.ObserveValues(77, 0d);
            state.TryAddChunk(77, 1, 2, new[]
            {
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, "あいう"),
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeChannel, "eye"),
                new OscFrameLayoutEntry(OscFrameLayoutEntryKind.GazeAttribute, "range=1,2,3,4")
            });
            Assert.That(state.TryTakeCompletedLayout(out _), Is.False);
            state.TryAddChunk(77, 0, 2, Entries("a", "b"));

            Assert.That(state.TryTakeCompletedLayout(out OscFrameLayout layout), Is.True);

            Assert.That(layout.Version, Is.EqualTo(77));
            Assert.That(layout.BlendShapeNames, Is.EqualTo(new[] { "a", "b", "あいう" }));
            Assert.That(layout.GazeChannels.Count, Is.EqualTo(1));
            Assert.That(layout.GazeChannels[0].Id, Is.EqualTo("eye"));
            Assert.That(layout.GazeChannels[0].Attributes, Is.EqualTo(new[] { "range=1,2,3,4" }));
        }

        [Test]
        public void MarkApplied_AllowsFramesOfThatVersionAndStopsRequesting()
        {
            var state = CreateState();
            state.RequestDestination = Destination;
            state.ObserveValues(77, 0d);
            Assert.That(state.CanApply(77), Is.False);

            state.MarkApplied(77);

            Assert.That(state.CanApply(77), Is.True);
            Assert.That(state.HasPending, Is.False);
            state.ObserveValues(77, 1d);
            Assert.That(state.TryCreateRequest(5d, out _), Is.False);
        }

        [Test]
        public void ObserveValues_VersionChangesAfterApplied_WaitsForNewVersionAndKeepsOldApplicable()
        {
            var state = CreateState();
            state.RequestDestination = Destination;
            state.ObserveValues(77, 0d);
            state.MarkApplied(77);

            state.ObserveValues(78, 1d);

            Assert.That(state.CanApply(78), Is.False);
            Assert.That(state.PendingVersion, Is.EqualTo(78));
            Assert.That(state.TryCreateRequest(1d, out byte[] request), Is.True);
            ReadRequest(request, out _, out int version, out _);
            Assert.That(version, Is.EqualTo(78));
        }

        [Test]
        public void TryTakeWarning_NotCompleteForWarnThreshold_WarnsOnce()
        {
            var state = CreateState();
            state.ObserveValues(77, 0d);
            state.ObserveValues(77, 2d);

            Assert.That(state.TryTakeWarning(2d), Is.False);
            state.ObserveValues(77, 3d);
            Assert.That(state.TryTakeWarning(3d), Is.True);
            Assert.That(state.TryTakeWarning(3.5d), Is.False);
        }

        private static OscIndexedSenderLayoutState CreateState()
        {
            return new OscIndexedSenderLayoutState(SenderUuid, Retry, WarnAfter);
        }

        private static OscFrameLayoutEntry[] Entries(params string[] blendShapeNames)
        {
            var entries = new OscFrameLayoutEntry[blendShapeNames.Length];
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                entries[i] = new OscFrameLayoutEntry(OscFrameLayoutEntryKind.BlendShape, blendShapeNames[i]);
            }

            return entries;
        }

        private static void ReadRequest(byte[] request, out Guid uuid, out int version, out List<int> indices)
        {
            var reader = new OscPacketReader(request);
            Assert.That(reader.TryReadNext(out OscMessageView view), Is.True);
            Assert.That(OscIndexedFrameCodec.IsLayoutRequestAddress(view.Address), Is.True);
            indices = new List<int>();
            Assert.That(OscIndexedFrameCodec.TryReadLayoutRequestMessage(in view, out uuid, out version, indices), Is.True);
        }
    }
}
