using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Edit プレビューの入口（<see cref="FacialTimelineEditorPreview.ApplyPreview"/>）が、Receiver の構成が欠けていても
    /// 例外にせず 1 回だけ警告してプレビューを継続することを固定する（Req 7.3）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class FacialTimelineEditorPreviewTests : SizedTestFixture
    {
        private GameObject _host;
        private TimelineAsset _timeline;
        private FacialTimelineBakeAsset _bake;

        [SetUp]
        public void SetUp()
        {
            TimelineEditorServices.EditWarningGate.ResetEpoch();
            FacialTimelineEditorPreview.ClearCache();
            _timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            var track = _timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            track.Bake = _bake;
        }

        [TearDown]
        public void TearDown()
        {
            FacialTimelineEditorPreview.ClearCache();
            TimelineEditorServices.EditWarningGate.ResetEpoch();
            if (_host != null)
            {
                Object.DestroyImmediate(_host);
            }

            if (_timeline != null)
            {
                Object.DestroyImmediate(_timeline);
            }

            if (_bake != null)
            {
                Object.DestroyImmediate(_bake);
            }
        }

        [Test]
        public void ApplyPreview_ReceiverWithoutController_DoesNotThrowAndWarnsOnce()
        {
            _host = new GameObject("PreviewHostWithoutController");
            var receiver = _host.AddComponent<FacialTimelineReceiver>();
            receiver.BakeAsset = _bake;

            // 文言ではなく「Warning が 1 件だけ出る」ことを固定する（2 回目以降は警告ゲートで抑止）。
            LogAssert.Expect(LogType.Warning, new Regex(".*"));

            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(receiver, _timeline, 0.5d));
            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(receiver, _timeline, 0.6d));
            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(receiver, _timeline, 0.7d));

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void ApplyPreview_NullReceiver_DoesNothing()
        {
            Assert.DoesNotThrow(() => FacialTimelineEditorPreview.ApplyPreview(null, _timeline, 0.5d));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void GatherProperties_ControllerWithoutRendererOverride_RegistersChildBlendShapes()
        {
            // Edit では FacialController が未初期化で SkinnedMeshRenderers（手動オーバーライド欄）が空のことが多い。
            // プレビューが書き込む子の SkinnedMeshRenderer を復元対象に登録しないと、プレビュー終了後も BlendShape が残る。
            _host = new GameObject("PreviewHost");
            _host.AddComponent<FacialController>();
            var receiver = _host.AddComponent<FacialTimelineReceiver>();
            var director = _host.AddComponent<PlayableDirector>();
            var face = new GameObject("Face");
            face.transform.SetParent(_host.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMeshWithBlendShapes("smile", "blink");

            TrackAsset track = _timeline.GetOutputTrack(0);
            director.playableAsset = _timeline;
            director.SetGenericBinding(track, receiver);
            var collector = new RecordingPropertyCollector();

            try
            {
                FacialTimelineEditorPreview.GatherProperties(director, track, collector);

                CollectionAssert.AreEquivalent(
                    new[] { "Face/blendShape.smile", "Face/blendShape.blink" },
                    collector.Registered);
            }
            finally
            {
                Object.DestroyImmediate(renderer.sharedMesh);
            }
        }

        [Test]
        public void GatherProperties_InactiveChildRenderer_IsNotRegistered()
        {
            // Play の FacialController はアクティブな子だけを集める。プレビューも同じ renderer に書き込み・復元しないと
            // BlendShape の並び（index）が Play とずれる。
            _host = new GameObject("PreviewHost");
            _host.AddComponent<FacialController>();
            var receiver = _host.AddComponent<FacialTimelineReceiver>();
            var director = _host.AddComponent<PlayableDirector>();
            var hidden = new GameObject("Hidden");
            hidden.transform.SetParent(_host.transform, false);
            var hiddenRenderer = hidden.AddComponent<SkinnedMeshRenderer>();
            hiddenRenderer.sharedMesh = CreateMeshWithBlendShapes("hiddenShape");
            hidden.SetActive(false);
            var face = new GameObject("Face");
            face.transform.SetParent(_host.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMeshWithBlendShapes("smile");

            TrackAsset track = _timeline.GetOutputTrack(0);
            director.playableAsset = _timeline;
            director.SetGenericBinding(track, receiver);
            var collector = new RecordingPropertyCollector();

            try
            {
                FacialTimelineEditorPreview.GatherProperties(director, track, collector);

                CollectionAssert.AreEquivalent(new[] { "Face/blendShape.smile" }, collector.Registered);
            }
            finally
            {
                Object.DestroyImmediate(hiddenRenderer.sharedMesh);
                Object.DestroyImmediate(renderer.sharedMesh);
            }
        }

        private static Mesh CreateMeshWithBlendShapes(params string[] names)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            var deltas = new Vector3[3];
            for (int i = 0; i < names.Length; i++)
            {
                mesh.AddBlendShapeFrame(names[i], 100f, deltas, null, null);
            }

            return mesh;
        }

        /// <summary>AddFromName で登録された「GameObject 名/プロパティ名」を記録する。</summary>
        private sealed class RecordingPropertyCollector : IPropertyCollector
        {
            public List<string> Registered { get; } = new List<string>();

            public void AddFromName<T>(GameObject obj, string name) where T : Component
            {
                Registered.Add(obj.name + "/" + name);
            }

            public void AddFromName(GameObject obj, string name)
            {
                Registered.Add(obj.name + "/" + name);
            }

            public void AddFromName(Component component, string name)
            {
                Registered.Add(component.gameObject.name + "/" + name);
            }

            public void PushActiveGameObject(GameObject gameObject)
            {
            }

            public void PopActiveGameObject()
            {
            }

            public void AddFromClip(AnimationClip clip)
            {
            }

            public void AddFromClips(IEnumerable<AnimationClip> clips)
            {
            }

            public void AddFromName<T>(string name) where T : Component
            {
            }

            public void AddFromName(string name)
            {
            }

            public void AddFromClip(GameObject obj, AnimationClip clip)
            {
            }

            public void AddFromClips(GameObject obj, IEnumerable<AnimationClip> clips)
            {
            }

            public void AddFromComponent(GameObject obj, Component component)
            {
            }

            public void AddObjectProperties(Object obj, AnimationClip clip)
            {
            }
        }
    }
}
