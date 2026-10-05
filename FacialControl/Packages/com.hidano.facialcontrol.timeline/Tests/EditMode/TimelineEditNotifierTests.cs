using System;
using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Editor.TrackEditors;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Timeline ウィンドウの Clip / Track 操作（TrackEditor / ClipEditor の override）が
    /// <see cref="TimelineEditorServices.ChangeWatcher"/> へ ClipEdit で MarkDirty を 1 回流すこと、
    /// トラック作成時に兄弟トラックの Bake 参照が補完されることを固定する（D8 経路 (a)）。
    /// 再ベイク口は Fake。保存済み TimelineAsset（AssetDatabase）を使うため Medium。
    /// </summary>
    [MediumTest]
    public sealed class TimelineEditNotifierTests : SizedTestFixture
    {
        private string _folderPath;
        private CountingRebakeExecutor _executor;
        private readonly List<UnityEngine.Object> _transient = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            string folderName = "TimelineEditNotifierTests_" + Guid.NewGuid().ToString("N");
            _folderPath = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            _executor = new CountingRebakeExecutor();
            TimelineEditorServices.Shutdown();
            TimelineEditorServices.RebakeExecutorOverride = _executor;
            TimelineEditorServices.EnsureInitialized();
        }

        [TearDown]
        public void TearDown()
        {
            TimelineEditorServices.Shutdown();
            TimelineEditorServices.RebakeExecutorOverride = null;
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
        }

        [Test]
        public void ExpressionClipEditor_OnClipChanged_MarksDirtyOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out FacialExpressionTrack track, out _);
            TimelineClip clip = FirstClip(track);

            new FacialExpressionClipEditor().OnClipChanged(clip);

            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ExpressionClipEditor_OnCreate_MarksDirtyOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out FacialExpressionTrack track, out _);
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();

            new FacialExpressionClipEditor().OnCreate(clip, track, null);

            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ExpressionTrackEditor_OnTrackChanged_MarksDirtyOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out FacialExpressionTrack track, out _);

            new FacialExpressionTrackEditor().OnTrackChanged(track);

            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ValueClipEditor_OnClipChanged_MarksDirtyOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out _, out FacialValueTrack valueTrack);
            TimelineClip clip = FirstClip(valueTrack);

            new FacialValueClipEditor().OnClipChanged(clip);

            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ValueClipEditor_OnCreate_MarksDirtyOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out _, out FacialValueTrack valueTrack);
            TimelineClip clip = valueTrack.CreateClip<FacialValueClip>();

            new FacialValueClipEditor().OnCreate(clip, valueTrack, null);

            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ValueTrackEditor_OnTrackChanged_MarksDirtyOnce()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out _, out FacialValueTrack valueTrack);

            new FacialValueTrackEditor().OnTrackChanged(valueTrack);

            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ValueTrackEditor_OnCreate_CompletesBakeFromSiblingsAndMarksDirty()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out FacialExpressionTrack expressionTrack, out _);
            FacialTimelineBakeAsset bake = CreateBake();
            expressionTrack.Bake = bake;
            FacialValueTrack created = timeline.CreateTrack<FacialValueTrack>(null, "osc:new");
            Assert.That(created.Bake, Is.Null, "前提: 新規トラックは Bake 参照なし");

            new FacialValueTrackEditor().OnCreate(created, null);

            Assert.That(created.Bake, Is.SameAs(bake));
            AssertMarkedOnce(timeline);
        }

        [Test]
        public void ExpressionTrackEditor_OnCreate_CopiedTrackWithForeignBake_ReplacedBySiblingBake()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out FacialExpressionTrack expressionTrack, out FacialValueTrack valueTrack);
            FacialTimelineBakeAsset bake = CreateBake();
            expressionTrack.Bake = bake;
            valueTrack.Bake = bake;
            FacialExpressionTrack copied = timeline.CreateTrack<FacialExpressionTrack>(null, "overlay");
            copied.Bake = CreateBake();

            new FacialExpressionTrackEditor().OnCreate(copied, expressionTrack);

            Assert.That(copied.Bake, Is.SameAs(bake));
            Assert.That(FacialTimelineBakeLocator.Locate(timeline, null).Status, Is.EqualTo(BakeLocateStatus.Found));
        }

        [Test]
        public void CompleteBakeReferenceFromSiblings_NoSiblingBake_LeavesNullAndReturnsFalse()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out _, out _);
            FacialValueTrack created = timeline.CreateTrack<FacialValueTrack>(null, "osc:new");

            bool changed = TimelineEditNotifier.CompleteBakeReferenceFromSiblings(created);

            Assert.That(changed, Is.False);
            Assert.That(created.Bake, Is.Null);
        }

        [Test]
        public void CompleteBakeReferenceFromSiblings_SiblingsConflict_DoesNotChoose()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out FacialExpressionTrack expressionTrack, out FacialValueTrack valueTrack);
            expressionTrack.Bake = CreateBake();
            valueTrack.Bake = CreateBake();
            FacialValueTrack created = timeline.CreateTrack<FacialValueTrack>(null, "osc:new");

            bool changed = TimelineEditNotifier.CompleteBakeReferenceFromSiblings(created);

            Assert.That(changed, Is.False, "兄弟の参照が割れているときは走査順で選ばない（再ベイクの修復に任せる）");
            Assert.That(created.Bake, Is.Null);
        }

        [Test]
        public void NotifyClipEdit_ServicesShutdown_ReturnsIgnoredWithoutThrowing()
        {
            TimelineAsset timeline = CreateSavedTimeline("A", out _, out _);
            TimelineEditorServices.Shutdown();

            MarkDirtyResult result = TimelineEditNotifier.NotifyClipEdit(timeline);

            Assert.That(result, Is.EqualTo(MarkDirtyResult.Ignored));
        }

        private void AssertMarkedOnce(TimelineAsset timeline)
        {
            Assert.That(TimelineEditorServices.ChangeWatcher.IsPending(timeline), Is.True, "Watcher の pending が立つ");
            TimelineEditorServices.ChangeWatcher.FlushNow();
            Assert.That(_executor.Calls, Is.EqualTo(new[] { timeline }), "再ベイクは 1 回");
        }

        private static TimelineClip FirstClip(TrackAsset track)
        {
            foreach (TimelineClip clip in track.GetClips())
            {
                return clip;
            }

            throw new InvalidOperationException("clip not found");
        }

        private TimelineAsset CreateSavedTimeline(string name, out FacialExpressionTrack expressionTrack, out FacialValueTrack valueTrack)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, _folderPath + "/" + name + ".playable");
            expressionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            TimelineClip clip = expressionTrack.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialExpressionClip)clip.asset).ExpressionId = "smile";

            valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            TimelineClip valueClip = valueTrack.CreateClip<FacialValueClip>();
            valueClip.start = 0d;
            valueClip.duration = 1d;

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        private FacialTimelineBakeAsset CreateBake()
        {
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            _transient.Add(bake);
            return bake;
        }

        private sealed class CountingRebakeExecutor : IRebakeExecutor
        {
            public readonly List<TimelineAsset> Calls = new List<TimelineAsset>();

            public RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
            {
                Calls.Add(timeline);
                bake = null;
                failureReason = string.Empty;
                return RebakeOutcome.NoChange;
            }
        }
    }
}
