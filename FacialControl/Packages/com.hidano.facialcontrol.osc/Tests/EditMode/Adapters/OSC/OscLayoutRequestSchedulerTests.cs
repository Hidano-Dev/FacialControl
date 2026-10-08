using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscLayoutRequestSchedulerTests : SizedTestFixture
    {
        private const double Retry = 0.5d;
        private const double WarnAfter = 3d;

        [Test]
        public void ObserveFrameVersion_UnknownToReceiver_RequestsImmediately()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);

            scheduler.ObserveFrameVersion(10, 100d);

            Assert.That(scheduler.HasPending, Is.True);
            Assert.That(scheduler.PendingVersion, Is.EqualTo(10));
            Assert.That(scheduler.IsReady(10), Is.False);
            Assert.That(scheduler.TryTakeRequest(100d), Is.True);
        }

        [Test]
        public void TryTakeRequest_WhilePending_RetriesOnlyAfterInterval()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);

            Assert.That(scheduler.TryTakeRequest(0d), Is.True);
            scheduler.ObserveFrameVersion(10, 0.1d);
            Assert.That(scheduler.TryTakeRequest(0.2d), Is.False);
            Assert.That(scheduler.TryTakeRequest(0.5d), Is.True);
            Assert.That(scheduler.TryTakeRequest(0.6d), Is.False);
            Assert.That(scheduler.TryTakeRequest(1.0d), Is.True);
        }

        [Test]
        public void TryTakeRequest_NothingObserved_ReturnsFalse()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);

            Assert.That(scheduler.TryTakeRequest(0d), Is.False);
            scheduler.ObserveFrameVersion(OscFrameLayoutVersion.Unknown, 0d);
            Assert.That(scheduler.TryTakeRequest(0d), Is.False);
        }

        [Test]
        public void MarkReady_PendingVersion_StopsRequestsAndAcceptsFrames()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);
            Assert.That(scheduler.TryTakeRequest(0d), Is.True);

            scheduler.MarkReady(10);

            Assert.That(scheduler.HasPending, Is.False);
            Assert.That(scheduler.IsReady(10), Is.True);
            Assert.That(scheduler.TryTakeRequest(5d), Is.False);
        }

        [Test]
        public void ObserveFrameVersion_VersionChangedAfterReady_RejectsNewFramesAndRequestsImmediately()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);
            scheduler.TryTakeRequest(0d);
            scheduler.MarkReady(10);

            scheduler.ObserveFrameVersion(11, 0.1d);

            Assert.That(scheduler.IsReady(11), Is.False);
            Assert.That(scheduler.IsReady(10), Is.True);
            Assert.That(scheduler.TryTakeRequest(0.1d), Is.True);
        }

        [Test]
        public void ObserveFrameVersion_VersionChangedWhilePending_RequestsImmediately()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);
            Assert.That(scheduler.TryTakeRequest(0d), Is.True);

            scheduler.ObserveFrameVersion(11, 0.1d);

            Assert.That(scheduler.PendingVersion, Is.EqualTo(11));
            Assert.That(scheduler.TryTakeRequest(0.1d), Is.True);
        }

        [Test]
        public void ObserveFrameVersion_BackToReadyVersion_ClearsPending()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);
            scheduler.MarkReady(10);
            scheduler.ObserveFrameVersion(11, 1d);

            scheduler.ObserveFrameVersion(10, 1.1d);

            Assert.That(scheduler.HasPending, Is.False);
            Assert.That(scheduler.TryTakeRequest(2d), Is.False);
        }

        [Test]
        public void TryTakeWarning_PendingPastThreshold_WarnsOnceUntilReadyAgain()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);

            Assert.That(scheduler.TryTakeWarning(2.9d), Is.False);
            Assert.That(scheduler.TryTakeWarning(3d), Is.True);
            Assert.That(scheduler.TryTakeWarning(10d), Is.False);

            scheduler.ObserveFrameVersion(11, 11d);
            Assert.That(scheduler.TryTakeWarning(20d), Is.False);

            scheduler.MarkReady(11);
            scheduler.ObserveFrameVersion(12, 30d);
            Assert.That(scheduler.TryTakeWarning(32d), Is.False);
            Assert.That(scheduler.TryTakeWarning(33d), Is.True);
        }

        [Test]
        public void TryTakeWarning_VersionKeepsChangingBeforeReady_MeasuresFromFirstMiss()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);
            scheduler.ObserveFrameVersion(11, 2d);

            Assert.That(scheduler.GetPendingSeconds(3d), Is.EqualTo(3d));
            Assert.That(scheduler.TryTakeWarning(3d), Is.True);
        }

        [Test]
        public void Reset_ForgetsReadyAndPendingVersions()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            scheduler.ObserveFrameVersion(10, 0d);
            scheduler.MarkReady(10);
            scheduler.ObserveFrameVersion(11, 1d);

            scheduler.Reset();

            Assert.That(scheduler.ReadyVersion, Is.EqualTo(OscFrameLayoutVersion.Unknown));
            Assert.That(scheduler.HasPending, Is.False);
            Assert.That(scheduler.IsReady(10), Is.False);
        }

        [Test]
        public void Constructor_NonPositiveRetryInterval_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new OscLayoutRequestScheduler(0d, WarnAfter));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OscLayoutRequestScheduler(double.NaN, WarnAfter));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OscLayoutRequestScheduler(Retry, -1d));
        }

        /// <summary>
        /// 要求 1 回目が消え、返信の 2 チャンクのうち 1 つが消えても、再要求で欠けたチャンクだけを取り直して揃う。
        /// </summary>
        [Test]
        public void Recovery_RequestAndOneReplyChunkLost_RerequestsOnlyMissingChunkUntilComplete()
        {
            var scheduler = new OscLayoutRequestScheduler(Retry, WarnAfter);
            var assembler = new OscFrameLayoutAssembler();
            var missing = new List<int>();
            OscFrameLayoutEntry[] chunk0 = OscFrameLayout.ToEntries(new[] { "Smile" }, null);
            OscFrameLayoutEntry[] chunk1 = OscFrameLayout.ToEntries(new[] { "まばたき" }, null);

            // 初回受信: 未知のバージョンなので即要求する（この要求は消える）。
            scheduler.ObserveFrameVersion(10, 0d);
            assembler.Reset(10);
            Assert.That(scheduler.TryTakeRequest(0d), Is.True);
            assembler.GetMissingChunkIndices(missing);
            Assert.That(missing, Is.Empty, "チャンク総数が分からない間は全チャンクを要求する");

            // 一定間隔後に再要求。返信のうちチャンク 0 だけが届く。
            Assert.That(scheduler.TryTakeRequest(0.3d), Is.False);
            Assert.That(scheduler.TryTakeRequest(0.5d), Is.True);
            Assert.That(assembler.TryAddChunk(10, 0, 2, chunk0), Is.True);
            Assert.That(scheduler.IsReady(10), Is.False);

            // 次の再要求ではチャンク 1 だけを求める。
            Assert.That(scheduler.TryTakeRequest(1.0d), Is.True);
            assembler.GetMissingChunkIndices(missing);
            Assert.That(missing, Is.EqualTo(new[] { 1 }));
            Assert.That(assembler.TryAddChunk(10, 1, 2, chunk1), Is.True);

            Assert.That(assembler.TryBuild(out OscFrameLayout layout), Is.True);
            scheduler.MarkReady(layout.Version);
            Assert.That(scheduler.IsReady(10), Is.True);
            Assert.That(scheduler.TryTakeRequest(2d), Is.False);
            Assert.That(scheduler.TryTakeWarning(10d), Is.False);
            Assert.That(layout.BlendShapeNames, Is.EqualTo(new[] { "Smile", "まばたき" }));
        }
    }
}
