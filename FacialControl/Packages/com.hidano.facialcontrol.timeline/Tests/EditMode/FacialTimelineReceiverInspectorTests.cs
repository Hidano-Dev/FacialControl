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
        // トラックの binding は書かない（自動 Bind の廃止。HID-408）
        // ================================================================

        [Test]
        public void CreateInspectorGUI_UnboundTracks_DoesNotWriteDirectorBinding()
        {
            var host = new GameObject("ReceiverInspectorTimelineHost");
            _created.Add(host);
            host.AddComponent<Animator>();
            host.AddComponent<FacialController>();
            var director = host.AddComponent<PlayableDirector>();
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            FacialCharacterTrack character = timeline.CreateTrack<FacialCharacterTrack>(null, "Facial Character");
            TrackAsset track = character.CreateChildTrack<FacialExpressionTrack>("emotion");
            director.playableAsset = timeline;
            FacialTimelineReceiver receiver = host.AddComponent<FacialTimelineReceiver>();
            receiver.DirectorOverride = director;
            int dirtyBefore = EditorUtility.GetDirtyCount(director);

            FacialTimelineReceiverInspector inspector = CreateInspector(receiver);
            inspector.CreateInspectorGUI();
            inspector.RequestEvaluation();
            inspector.RefreshNow();

            Assert.That(director.GetGenericBinding(character), Is.Null);
            Assert.That(director.GetGenericBinding(track), Is.Null);
            Assert.That(EditorUtility.GetDirtyCount(director), Is.EqualTo(dirtyBefore), "表示しただけで Director を書き換えない");
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
