using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineEditChangeWatcher"/> が変更通知を Timeline 単位に合流させ、デバウンス後に再ベイクを 1 回だけ実行し、
    /// 結果に応じて BakeUpdated / RebakeFailed を出すことを固定する。再ベイク口と時計は Fake。
    /// 保存済み TimelineAsset（AssetDatabase）を使うため Medium。
    /// </summary>
    [MediumTest]
    public sealed class TimelineEditChangeWatcherTests : SizedTestFixture
    {
        private string _folderPath;
        private double _now;
        private FakeRebakeExecutor _executor;
        private TimelineEditChangeWatcher _watcher;
        private int _tickRequests;
        private int _tickReleases;
        private bool _playModeTransition;
        private readonly List<UnityEngine.Object> _transient = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            string folderName = "TimelineEditChangeWatcherTests_" + Guid.NewGuid().ToString("N");
            _folderPath = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            _now = 100d;
            _tickRequests = 0;
            _tickReleases = 0;
            _playModeTransition = false;
            _executor = new FakeRebakeExecutor();
            _watcher = new TimelineEditChangeWatcher(
                _executor,
                () => _now,
                () => _tickRequests++,
                () => _tickReleases++)
            {
                IsPlayModeTransition = () => _playModeTransition,
                RefreshTimelineWindow = () => { },
            };
        }

        [TearDown]
        public void TearDown()
        {
            _watcher?.Dispose();
            _watcher = null;

            for (int i = 0; i < _transient.Count; i++)
            {
                if (_transient[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_transient[i]);
                }
            }

            _transient.Clear();

            if (AssetDatabase.IsValidFolder(_folderPath))
            {
                AssetDatabase.DeleteAsset(_folderPath);
            }
        }

        [Test]
        public void MarkDirty_ThenTickAfterDebounce_RebakesOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");

            MarkDirtyResult result = _watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            _now += 0.1d;
            _watcher.Tick();

            Assert.That(result, Is.EqualTo(MarkDirtyResult.Queued));
            Assert.That(_executor.Calls.Count, Is.EqualTo(0), "デバウンス経過前は実行しない");
            Assert.That(_watcher.IsPending(timeline), Is.True);

            _now += _watcher.DebounceSeconds;
            _watcher.Tick();

            Assert.That(_executor.Calls, Is.EqualTo(new[] { timeline }));
            Assert.That(_watcher.IsPending(timeline), Is.False);
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void DebounceSeconds_Default_IsPoint3()
        {
            Assert.That(_watcher.DebounceSeconds, Is.EqualTo(0.3d));
        }

        [Test]
        public void Tick_RebakeReturnsNoChange_DoesNotRaiseBakeUpdated()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            _executor.NextOutcome = RebakeOutcome.NoChange;
            int updated = 0;
            _watcher.BakeUpdated += (t, b, r) => updated++;

            _watcher.MarkDirty(timeline, TimelineDirtyReason.ObjectChange);
            _watcher.FlushNow();

            Assert.That(_executor.Calls.Count, Is.EqualTo(1));
            Assert.That(updated, Is.EqualTo(0));
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void MarkDirty_TenTimesInARow_RebakesOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            var results = new List<MarkDirtyResult>();

            for (int i = 0; i < 10; i++)
            {
                results.Add(_watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit));
                _now += 0.016d;
                _watcher.Tick();
            }

            _now += _watcher.DebounceSeconds;
            _watcher.Tick();
            _now += _watcher.DebounceSeconds;
            _watcher.Tick();

            Assert.That(_executor.Calls.Count, Is.EqualTo(1));
            Assert.That(results[0], Is.EqualTo(MarkDirtyResult.Queued));
            for (int i = 1; i < results.Count; i++)
            {
                Assert.That(results[i], Is.EqualTo(MarkDirtyResult.Coalesced));
            }
        }

        [Test]
        public void MarkDirty_DuringRebake_RerunsOnceAfterCompletion()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            var duringResults = new List<MarkDirtyResult>();
            _executor.OnRebake = t =>
            {
                if (_executor.Calls.Count == 1)
                {
                    duringResults.Add(_watcher.MarkDirty(t, TimelineDirtyReason.ClipEdit));
                    duringResults.Add(_watcher.MarkDirty(t, TimelineDirtyReason.ClipEdit));
                    duringResults.Add(_watcher.MarkDirty(t, TimelineDirtyReason.UndoRedo));
                }
            };

            _watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            _now += _watcher.DebounceSeconds;
            _watcher.Tick();

            Assert.That(_executor.Calls.Count, Is.EqualTo(1), "実行中の MarkDirty で即座に再入しない");
            Assert.That(_watcher.IsPending(timeline), Is.True, "完了後にもう 1 回が予約される");
            Assert.That(duringResults, Is.All.EqualTo(MarkDirtyResult.Coalesced));

            _now += _watcher.DebounceSeconds;
            _watcher.Tick();
            _now += _watcher.DebounceSeconds;
            _watcher.Tick();

            Assert.That(_executor.Calls.Count, Is.EqualTo(2), "完了後に 1 回だけ再実行する");
            Assert.That(_watcher.IsPending(timeline), Is.False);
        }

        [Test]
        public void Tick_RebakeFails_RaisesRebakeFailedAndKeepsPreviousBake()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            FacialTimelineBakeAsset previous = AddBake(timeline);
            _executor.NextOutcome = RebakeOutcome.Failed;
            _executor.NextFailureReason = "boom";
            string failedReason = null;
            TimelineAsset failedTimeline = null;
            int updated = 0;
            _watcher.RebakeFailed += (t, reason) =>
            {
                failedTimeline = t;
                failedReason = reason;
            };
            _watcher.BakeUpdated += (t, b, r) => updated++;

            _watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            _watcher.FlushNow();

            Assert.That(failedTimeline, Is.SameAs(timeline));
            Assert.That(failedReason, Is.EqualTo("boom"));
            Assert.That(updated, Is.EqualTo(0));
            Assert.That(FacialTimelineBakeLocator.Locate(timeline, null).Bake, Is.SameAs(previous), "前回 Bake を保持する");
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void Tick_RebakeThrows_TreatedAsFailure()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            _executor.OnRebake = t => throw new InvalidOperationException("explode");
            string failedReason = null;
            _watcher.RebakeFailed += (t, reason) => failedReason = reason;

            _watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            _watcher.FlushNow();

            Assert.That(failedReason, Does.Contain("explode"));
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void MarkDirty_ProfileChanged_RebakesAndReportsReason()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            _executor.NextOutcome = RebakeOutcome.Rebaked;
            var reasons = new List<TimelineDirtyReason>();
            _watcher.BakeUpdated += (t, b, r) => reasons.Add(r);

            _watcher.MarkDirty(timeline, TimelineDirtyReason.ProfileChanged);
            _now += _watcher.DebounceSeconds;
            _watcher.Tick();

            Assert.That(_executor.Calls.Count, Is.EqualTo(1));
            Assert.That(reasons, Is.EqualTo(new[] { TimelineDirtyReason.ProfileChanged }));
        }

        [Test]
        public void MarkDirty_BakeReferenceInconsistent_ReferencesRepaired_RaisesBakeUpdated()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            FacialTimelineBakeAsset bake = AddBake(timeline);
            _executor.NextOutcome = RebakeOutcome.ReferencesRepaired;
            _executor.NextBake = bake;
            FacialTimelineBakeAsset updatedBake = null;
            TimelineDirtyReason? updatedReason = null;
            _watcher.BakeUpdated += (t, b, r) =>
            {
                updatedBake = b;
                updatedReason = r;
            };

            _watcher.MarkDirty(timeline, TimelineDirtyReason.BakeReferenceInconsistent);
            _watcher.FlushNow();

            Assert.That(updatedBake, Is.SameAs(bake));
            Assert.That(updatedReason, Is.EqualTo(TimelineDirtyReason.BakeReferenceInconsistent));
        }

        [Test]
        public void MarkDirty_UnsavedTimeline_ReturnsUnsavedTimelineAndDoesNotQueue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _transient.Add(timeline);
            timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");

            MarkDirtyResult result = _watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            _watcher.FlushNow();

            Assert.That(result, Is.EqualTo(MarkDirtyResult.UnsavedTimeline));
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
            Assert.That(_watcher.IsPending(timeline), Is.False);
            Assert.That(_tickRequests, Is.EqualTo(0));
            Assert.That(_executor.Calls.Count, Is.EqualTo(0));
        }

        [Test]
        public void MarkDirty_NullOrNonFacialOrPlayModeTransition_ReturnsIgnored()
        {
            TimelineAsset nonFacial = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(nonFacial, _folderPath + "/NonFacial.playable");
            TimelineAsset facial = CreateSavedTimeline("A");

            Assert.That(_watcher.MarkDirty(null, TimelineDirtyReason.ClipEdit), Is.EqualTo(MarkDirtyResult.Ignored));
            Assert.That(_watcher.MarkDirty(nonFacial, TimelineDirtyReason.ClipEdit), Is.EqualTo(MarkDirtyResult.Ignored));

            _playModeTransition = true;
            Assert.That(_watcher.MarkDirty(facial, TimelineDirtyReason.ProfileChanged), Is.EqualTo(MarkDirtyResult.Ignored));
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void PendingCount_ZeroToOneAndOneToZero_RequestsAndReleasesTickOnce()
        {
            TimelineAsset a = CreateSavedTimeline("A");
            TimelineAsset b = CreateSavedTimeline("B");

            _watcher.MarkDirty(a, TimelineDirtyReason.ClipEdit);
            _watcher.MarkDirty(b, TimelineDirtyReason.ClipEdit);
            _watcher.MarkDirty(a, TimelineDirtyReason.ClipEdit);

            Assert.That(_tickRequests, Is.EqualTo(1));
            Assert.That(_tickReleases, Is.EqualTo(0));
            Assert.That(_watcher.PendingCount, Is.EqualTo(2));

            _now += _watcher.DebounceSeconds;
            _watcher.Tick();

            Assert.That(_executor.Calls.Count, Is.EqualTo(2));
            Assert.That(_tickRequests, Is.EqualTo(1));
            Assert.That(_tickReleases, Is.EqualTo(1));

            _watcher.MarkDirty(a, TimelineDirtyReason.ClipEdit);
            Assert.That(_tickRequests, Is.EqualTo(2), "再び 0 → 1 で要求する");
        }

        [Test]
        public void Dispose_DiscardsPendingAndReleasesTick()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            _watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);

            _watcher.Dispose();
            _now += 10d;
            _watcher.Tick();
            _watcher.FlushNow();

            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
            Assert.That(_watcher.IsPending(timeline), Is.False);
            Assert.That(_tickReleases, Is.EqualTo(1));
            Assert.That(_executor.Calls.Count, Is.EqualTo(0));
            Assert.That(_watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit), Is.EqualTo(MarkDirtyResult.Ignored));

            _watcher.Dispose();
            Assert.That(_tickReleases, Is.EqualTo(1), "二重 Dispose は no-op");
        }

        [Test]
        public void TrackProfile_MarkProfileChanged_QueuesTrackedTimelinesOnly()
        {
            TimelineAsset a = CreateSavedTimeline("A");
            TimelineAsset b = CreateSavedTimeline("B");
            TimelineAsset c = CreateSavedTimeline("C");
            FacialCharacterProfileSO profile = CreateSavedProfile("P");
            FacialCharacterProfileSO other = CreateSavedProfile("Q");

            _watcher.TrackProfile(a, profile);
            _watcher.TrackProfile(b, profile);
            _watcher.TrackProfile(c, other);

            int marked = _watcher.MarkProfileChanged(profile);

            Assert.That(marked, Is.EqualTo(2));
            Assert.That(_watcher.IsPending(a), Is.True);
            Assert.That(_watcher.IsPending(b), Is.True);
            Assert.That(_watcher.IsPending(c), Is.False);
            Assert.That(_watcher.GetTrackedTimelines(profile), Is.EquivalentTo(new[] { a, b }));
        }

        [Test]
        public void TrackProfile_RetrackToAnotherProfile_RemovesOldReverseLookup()
        {
            TimelineAsset a = CreateSavedTimeline("A");
            FacialCharacterProfileSO profile = CreateSavedProfile("P");
            FacialCharacterProfileSO other = CreateSavedProfile("Q");

            _watcher.TrackProfile(a, profile);
            _watcher.TrackProfile(a, other);

            Assert.That(_watcher.GetTrackedTimelines(profile), Is.Empty);
            Assert.That(_watcher.GetTrackedTimelines(other), Is.EqualTo(new[] { a }));
            Assert.That(_watcher.TrackedTimelines, Is.EqualTo(new[] { a }));
        }

        [Test]
        public void FlushNow_RunsAllPendingWithoutWaitingForDebounce()
        {
            TimelineAsset a = CreateSavedTimeline("A");
            TimelineAsset b = CreateSavedTimeline("B");
            _watcher.MarkDirty(a, TimelineDirtyReason.ClipEdit);
            _watcher.MarkDirty(b, TimelineDirtyReason.UndoRedo);

            _watcher.FlushNow();

            Assert.That(_executor.Calls, Is.EquivalentTo(new[] { a, b }));
            Assert.That(_watcher.PendingCount, Is.EqualTo(0));
            Assert.That(_tickReleases, Is.EqualTo(1));
        }

        private TimelineAsset CreateSavedTimeline(string name)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, _folderPath + "/" + name + ".playable");
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialExpressionClip)clip.asset).ExpressionId = "smile";
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        private FacialCharacterProfileSO CreateSavedProfile(string name)
        {
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            AssetDatabase.CreateAsset(profile, _folderPath + "/" + name + ".asset");
            return profile;
        }

        private static FacialTimelineBakeAsset AddBake(TimelineAsset timeline)
        {
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.name = "FacialTimelineBake";
            AssetDatabase.AddObjectToAsset(bake, timeline);
            BakeReferenceWriter.Apply(timeline, bake);
            AssetDatabase.SaveAssets();
            return bake;
        }

        private sealed class FakeRebakeExecutor : IRebakeExecutor
        {
            public readonly List<TimelineAsset> Calls = new List<TimelineAsset>();

            public RebakeOutcome NextOutcome { get; set; } = RebakeOutcome.Rebaked;

            public FacialTimelineBakeAsset NextBake { get; set; }

            public string NextFailureReason { get; set; } = string.Empty;

            public Action<TimelineAsset> OnRebake { get; set; }

            public RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
            {
                Calls.Add(timeline);
                OnRebake?.Invoke(timeline);
                bake = NextBake;
                failureReason = NextOutcome == RebakeOutcome.Failed ? NextFailureReason : string.Empty;
                return NextOutcome;
            }
        }
    }
}
