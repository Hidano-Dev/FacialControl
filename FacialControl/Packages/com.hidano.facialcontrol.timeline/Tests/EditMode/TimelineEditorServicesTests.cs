using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.AutoExport;
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
    /// <see cref="TimelineEditorServices"/> が Unity イベント購読の唯一の所有者として、冪等な初期化 / Shutdown・
    /// pending がある間だけの update 購読・配送規則（Undo / Exported / ExitingEditMode）を守ることを固定する。
    /// 再ベイク口は Fake。AssetDatabase と profile.json を使うため Medium。
    /// </summary>
    [MediumTest]
    public sealed class TimelineEditorServicesTests : SizedTestFixture
    {
        private const int FixedSubscriptionCount = 6;

        private string _folderPath;
        private FakeRebakeExecutor _executor;
        private readonly List<string> _callOrder = new List<string>();
        private readonly List<UnityEngine.Object> _transient = new List<UnityEngine.Object>();
        private readonly List<string> _exportDirs = new List<string>();

        [SetUp]
        public void SetUp()
        {
            string folderName = "TimelineEditorServicesTests_" + Guid.NewGuid().ToString("N");
            _folderPath = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            _callOrder.Clear();
            _executor = new FakeRebakeExecutor(_callOrder);

            TimelineEditorServices.Shutdown();
            TimelineEditorServices.RebakeExecutorOverride = _executor;
            TimelineEditorServices.ExitingEditModeProcessor = () => _callOrder.Add("process");
            TimelineEditorServices.EnsureInitialized();
        }

        [TearDown]
        public void TearDown()
        {
            TimelineEditorServices.Shutdown();
            TimelineEditorServices.RebakeExecutorOverride = null;
            TimelineEditorServices.ExitingEditModeProcessor = null;
            TimelineEditorServices.EnsureInitialized();

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

            for (int i = 0; i < _exportDirs.Count; i++)
            {
                if (Directory.Exists(_exportDirs[i]))
                {
                    Directory.Delete(_exportDirs[i], recursive: true);
                }

                if (File.Exists(_exportDirs[i] + ".meta"))
                {
                    File.Delete(_exportDirs[i] + ".meta");
                }
            }

            _exportDirs.Clear();
            AssetDatabase.Refresh();
        }

        [Test]
        public void EnsureInitialized_CalledTwice_SubscriptionCountUnchanged()
        {
            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            int before = TimelineEditorServices.ActiveSubscriptionCount;

            TimelineEditorServices.EnsureInitialized();

            Assert.That(TimelineEditorServices.IsInitialized, Is.True);
            Assert.That(before, Is.EqualTo(FixedSubscriptionCount));
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount));
            Assert.That(TimelineEditorServices.ChangeWatcher, Is.SameAs(watcher));
        }

        [Test]
        public void Shutdown_UnsubscribesAllAndLaterChangesDoNotRebake()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            watcher.TrackProfile(timeline, CreateSavedProfile("P_" + Guid.NewGuid().ToString("N")));
            watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);

            TimelineEditorServices.Shutdown();

            Assert.That(TimelineEditorServices.IsInitialized, Is.False);
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(0));
            Assert.That(watcher.IsPending(timeline), Is.False, "pending は破棄される");

            PerformUndoableEdit(timeline);
            watcher.MarkDirty(timeline, TimelineDirtyReason.ObjectChange);
            watcher.FlushNow();

            Assert.That(_executor.Calls.Count, Is.EqualTo(0));

            TimelineEditorServices.Shutdown();
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(0), "二重 Shutdown は no-op");
        }

        [Test]
        public void ShutdownThenEnsureInitialized_OneChange_RebakesOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");

            TimelineEditorServices.Shutdown();
            TimelineEditorServices.EnsureInitialized();
            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            watcher.TrackProfile(timeline, CreateSavedProfile("P_" + Guid.NewGuid().ToString("N")));

            PerformUndoableEdit(timeline);
            watcher.FlushNow();

            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount));
            Assert.That(_executor.Calls, Is.EqualTo(new[] { timeline }));
        }

        [Test]
        public void MarkDirty_UnsavedTimeline_IsSkipped()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _transient.Add(timeline);
            timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");

            MarkDirtyResult result = TimelineEditorServices.ChangeWatcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            TimelineEditorServices.ChangeWatcher.FlushNow();

            Assert.That(result, Is.EqualTo(MarkDirtyResult.UnsavedTimeline));
            Assert.That(TimelineEditorServices.ChangeWatcher.IsPending(timeline), Is.False);
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount));
            Assert.That(_executor.Calls.Count, Is.EqualTo(0));
        }

        [Test]
        public void MarkDirty_TenTimesThenFlushNow_RebakesOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");

            for (int i = 0; i < 10; i++)
            {
                TimelineEditorServices.ChangeWatcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            }

            TimelineEditorServices.ChangeWatcher.FlushNow();

            Assert.That(_executor.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void UpdateSubscription_OnlyWhilePending()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount), "pending が無い間は update を購読しない");

            TimelineEditorServices.ChangeWatcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount + 1));

            TimelineEditorServices.ChangeWatcher.FlushNow();
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount));
        }

        [Test]
        public void ExitingEditMode_FlushesWatcherBeforeSerialProcessing()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            TimelineEditorServices.ChangeWatcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);

            TimelineEditorServices.DispatchPlayModeStateChanged(PlayModeStateChange.ExitingEditMode);

            Assert.That(_callOrder, Is.EqualTo(new[] { "rebake", "process" }));
            Assert.That(TimelineEditorServices.ChangeWatcher.PendingCount, Is.EqualTo(0));
        }

        [Test]
        public void ExitingPlayMode_DiscardsPending()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            TimelineEditorServices.ChangeWatcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);

            TimelineEditorServices.DispatchPlayModeStateChanged(PlayModeStateChange.ExitingPlayMode);

            Assert.That(TimelineEditorServices.ChangeWatcher.PendingCount, Is.EqualTo(0));
            Assert.That(TimelineEditorServices.ActiveSubscriptionCount, Is.EqualTo(FixedSubscriptionCount));
            Assert.That(_executor.Calls.Count, Is.EqualTo(0));
        }

        [Test]
        public void Exported_WhenProfileJsonWritten_MarksTrackedTimelinePending()
        {
            TimelineAsset timeline = CreateSavedTimeline("A");
            TimelineAsset untracked = CreateSavedTimeline("B");
            FacialCharacterProfileSO profile = CreateSavedProfile("ServicesProfile_" + Guid.NewGuid().ToString("N"));
            _exportDirs.Add(Path.GetDirectoryName(FacialCharacterProfileSO.GetStreamingAssetsProfilePath(profile.CharacterAssetName)));
            TimelineEditorServices.ChangeWatcher.TrackProfile(timeline, profile);

            bool written = FacialCharacterProfileAutoExporter.ExportIfEnabled(profile);

            Assert.That(written, Is.True, "前提: profile.json が実際に書かれる");
            Assert.That(TimelineEditorServices.ChangeWatcher.IsPending(timeline), Is.True);
            Assert.That(TimelineEditorServices.ChangeWatcher.IsPending(untracked), Is.False);
        }

        private static void PerformUndoableEdit(TimelineAsset timeline)
        {
            Undo.IncrementCurrentGroup();
            Undo.RecordObject(timeline, "Edit Timeline");
            timeline.durationMode = timeline.durationMode == TimelineAsset.DurationMode.BasedOnClips
                ? TimelineAsset.DurationMode.FixedLength
                : TimelineAsset.DurationMode.BasedOnClips;
            Undo.IncrementCurrentGroup();
            Undo.PerformUndo();
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
            profile.Layers.Add(new LayerDefinitionSerializable { name = "emotion", priority = 0 });
            profile.Expressions.Add(new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = "emotion",
                transitionDuration = 0.25f,
            });
            AssetDatabase.CreateAsset(profile, _folderPath + "/" + name + ".asset");
            AssetDatabase.SaveAssets();
            return profile;
        }

        private sealed class FakeRebakeExecutor : IRebakeExecutor
        {
            private readonly List<string> _order;

            public FakeRebakeExecutor(List<string> order)
            {
                _order = order;
            }

            public readonly List<TimelineAsset> Calls = new List<TimelineAsset>();

            public RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
            {
                Calls.Add(timeline);
                _order.Add("rebake");
                bake = null;
                failureReason = string.Empty;
                return RebakeOutcome.NoChange;
            }
        }
    }
}
