using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
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
