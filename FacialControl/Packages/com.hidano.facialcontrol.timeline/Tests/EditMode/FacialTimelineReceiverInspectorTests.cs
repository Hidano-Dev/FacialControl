using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Receiver Inspector の生成・保存・破棄の smoke。購読が CreateInspectorGUI で登録され、OnDisable / 二重解除で
    /// 残らないこと、target 破棄後に handler が呼ばれても例外にならないことを固定する（見た目は検証しない）。
    /// </summary>
    [MediumTest]
    public sealed class FacialTimelineReceiverInspectorTests : SizedTestFixture
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        [Test]
        public void CreateInspectorGUI_BuildsSectionsAndSubscribes()
        {
            FacialTimelineReceiver receiver = CreateReceiver(withDirector: false);
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);

            VisualElement root = inspector.CreateInspectorGUI();

            Assert.That(root, Is.Not.Null);
            Assert.That(root.Q<Button>(FacialTimelineReceiverInspector.AssignBindingsButtonName), Is.Not.Null);
            Assert.That(root.Q<Button>(FacialTimelineReceiverInspector.RebakeButtonName), Is.Not.Null);
            Assert.That(root.Q<Button>(FacialTimelineReceiverInspector.RemoveLegacyButtonName), Is.Not.Null);
            Assert.That(root.Q(FacialTimelineReceiverInspector.DiagnosticsContainerName).childCount, Is.GreaterThan(0));
            Assert.That(receiver.Diagnostics.Contains(TimelineDiagnosticCode.DirectorMissing), Is.True, "Edit で静的診断が評価される");
            Assert.That(inspector.SubscriptionCount, Is.GreaterThan(0));
        }

        [Test]
        public void DestroyInspector_UnsubscribesAndDoubleUnsubscribeIsNoop()
        {
            FacialTimelineReceiver receiver = CreateReceiver(withDirector: true);
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();

            Object.DestroyImmediate(inspector);

            Assert.That(inspector.SubscriptionCount, Is.EqualTo(0));
            Assert.DoesNotThrow(() => inspector.Unsubscribe());
            Assert.That(inspector.SubscriptionCount, Is.EqualTo(0));
        }

        [Test]
        public void AttachAfterDetach_ResubscribesOnceWithoutDoubleRegistration()
        {
            FacialTimelineReceiver receiver = CreateReceiver(withDirector: true);
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();
            int subscribed = inspector.SubscriptionCount;
            Assert.That(subscribed, Is.GreaterThan(0));

            inspector.HandleAttachToPanel();
            Assert.That(inspector.SubscriptionCount, Is.EqualTo(subscribed), "初回 attach では二重登録しない");

            inspector.HandleDetachFromPanel();
            Assert.That(inspector.SubscriptionCount, Is.EqualTo(0));

            inspector.HandleAttachToPanel();
            Assert.That(inspector.SubscriptionCount, Is.EqualTo(subscribed), "Detach で解除した購読を再 attach で戻す");

            inspector.HandleAttachToPanel();
            Assert.That(inspector.SubscriptionCount, Is.EqualTo(subscribed), "二重登録しない");
        }

        [Test]
        public void AttachAfterExplicitUnsubscribe_DoesNotResubscribe()
        {
            FacialTimelineReceiver receiver = CreateReceiver(withDirector: true);
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();

            inspector.Unsubscribe();
            inspector.HandleAttachToPanel();

            Assert.That(inspector.SubscriptionCount, Is.EqualTo(0), "Detach 以外（OnDisable 等）で解除した後は再登録しない");
        }

        [Test]
        public void SaveAndUndoWhileOpen_DoesNotThrow()
        {
            FacialTimelineReceiver receiver = CreateReceiver(withDirector: true);
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();

            Assert.DoesNotThrow(() =>
            {
                Undo.IncrementCurrentGroup();
                Undo.RecordObject(receiver, "Edit Receiver");
                receiver.BakeAsset = null;
                AssetDatabase.SaveAssets();
                Undo.PerformUndo();
                inspector.RefreshNow();
            });
        }

        [Test]
        public void HandlersAfterTargetDestroyed_DoNotThrow()
        {
            FacialTimelineReceiver receiver = CreateReceiver(withDirector: true);
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();

            Object.DestroyImmediate(receiver.gameObject);

            Assert.DoesNotThrow(() => inspector.RefreshNow());
            Assert.DoesNotThrow(() => Undo.PerformUndo());
        }

        // ================================================================
        // Edit の Track binding 自動設定と Undo（9.6 是正レビュー指摘）
        // ================================================================

        [Test]
        public void UndoAutoAssign_ThenUndoTriggeredReevaluation_KeepsBindingNullAndRedoRestoresIt()
        {
            (FacialTimelineReceiver receiver, PlayableDirector director, TrackAsset track) = CreateReceiverWithTimeline();
            Undo.IncrementCurrentGroup();
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();
            Assert.That(director.GetGenericBinding(track), Is.SameAs(receiver), "fixture: 開いた時点で未設定トラックが自動設定される");

            Undo.PerformUndo();
            inspector.RefreshNow();

            Assert.That(director.GetGenericBinding(track), Is.Null, "Undo 起点の再評価で binding を書き直さない");

            Undo.PerformRedo();
            inspector.RefreshNow();

            Assert.That(director.GetGenericBinding(track), Is.SameAs(receiver), "Redo で自動設定が戻る（Redo 履歴が消えていない）");
        }

        [Test]
        public void ReevaluatingWhileOpen_AfterAutoAssign_DoesNotWriteDirectorAgain()
        {
            (FacialTimelineReceiver receiver, PlayableDirector director, TrackAsset track) = CreateReceiverWithTimeline();
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();
            Assert.That(director.GetGenericBinding(track), Is.SameAs(receiver));

            // 利用者が（Undo を使わずに）binding を外した後、Inspector を開いたまま再評価が何度起きても書き直さない。
            director.SetGenericBinding(track, null);
            int dirtyBefore = EditorUtility.GetDirtyCount(director);
            for (int i = 0; i < 3; i++)
            {
                inspector.RequestEvaluation();
                inspector.RefreshNow();
            }

            Assert.That(director.GetGenericBinding(track), Is.Null, "自動設定は Inspector × (Director, Timeline) ごとに 1 回だけ");
            Assert.That(EditorUtility.GetDirtyCount(director), Is.EqualTo(dirtyBefore), "Undo 記録（Director への書き込み）が増えない");
        }

        [Test]
        public void ReevaluatingWithDifferentTimeline_AutoAssignsOnceForNewPair()
        {
            (FacialTimelineReceiver receiver, PlayableDirector director, TrackAsset _) = CreateReceiverWithTimeline();
            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();

            var otherTimeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(otherTimeline);
            TrackAsset otherTrack = otherTimeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            director.playableAsset = otherTimeline;
            inspector.RequestEvaluation();
            inspector.RefreshNow();

            Assert.That(director.GetGenericBinding(otherTrack), Is.SameAs(receiver), "TimelineAsset が変わったら再び 1 回自動設定する");
        }

        private (FacialTimelineReceiver receiver, PlayableDirector director, TrackAsset track) CreateReceiverWithTimeline()
        {
            var host = new GameObject("ReceiverInspectorTimelineHost");
            _created.Add(host);
            host.AddComponent<Animator>();
            host.AddComponent<FacialController>();
            var director = host.AddComponent<PlayableDirector>();
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            TrackAsset track = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            director.playableAsset = timeline;
            FacialTimelineReceiver receiver = host.AddComponent<FacialTimelineReceiver>();
            return (receiver, director, track);
        }

        private FacialTimelineReceiver CreateReceiver(bool withDirector)
        {
            var host = new GameObject("ReceiverInspectorHost");
            _created.Add(host);
            host.AddComponent<Animator>();
            host.AddComponent<FacialController>();
            if (withDirector)
            {
                host.AddComponent<PlayableDirector>();
            }

            return host.AddComponent<FacialTimelineReceiver>();
        }

        private FacialTimelineReceiverInspector CreateInspector(FacialTimelineReceiver receiver)
        {
            var inspector = (FacialTimelineReceiverInspector)UnityEditor.Editor.CreateEditor(receiver);
            _created.Add(inspector);
            return inspector;
        }
    }
}
