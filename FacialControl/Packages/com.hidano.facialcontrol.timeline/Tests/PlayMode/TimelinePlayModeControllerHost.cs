using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// 既存 PlayMode fixture 用の最小ホスト。Receiver がセッションを開始できるよう、Animator + BlendShape 付きメッシュを持つ
    /// GameObject に FacialController を置き、指定 Profile で初期化する。
    /// </summary>
    internal static class TimelinePlayModeControllerHost
    {
        public static GameObject Create(string name, FacialProfile profile, IReadOnlyList<string> blendShapeNames)
        {
            var root = new GameObject(name);
            root.AddComponent<Animator>();

            var face = new GameObject("Face");
            face.transform.SetParent(root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            var mesh = new Mesh
            {
                name = name + "_Mesh",
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, new Vector3[3], null, null);
            }

            renderer.sharedMesh = mesh;

            var controller = root.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);
            Assert.That(controller.IsInitialized, Is.True, "fixture: FacialController が初期化できること");
            return root;
        }

        public static void Destroy(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            SkinnedMeshRenderer renderer = root.GetComponentInChildren<SkinnedMeshRenderer>();
            Mesh mesh = renderer != null ? renderer.sharedMesh : null;
            UnityEngine.Object.DestroyImmediate(root);
            if (mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(mesh);
            }
        }
    }

    /// <summary>
    /// 既存 PlayMode fixture 共通の再生リグ。FacialController（ホスト）+ Receiver + Timeline binding（controller の実 registry）+
    /// Manual 更新の Director を組み、Mixer の最初の評価でセッションを開始する。
    /// </summary>
    /// <remarks>
    /// レイヤーは Profile に Timeline 専用の設定を書かず、トラック名から <see cref="TimelineChannelDeriver"/> で自動導出される。
    /// リグは同じ導出を直接計算して前提（一致レイヤー）を固定し、Receiver 内部の Connector がその全レイヤーを
    /// 接続したことを診断コード（<see cref="TimelineDiagnosticCode.LayerConnected"/>）で確認する。private への reflection は使わない。
    /// </remarks>
    internal sealed class TimelinePlayModeRig : IDisposable
    {
        private readonly TimelineAdapterBinding _binding;
        private readonly GameObject _hostObject;
        private readonly GameObject _directorObject;

        private TimelinePlayModeRig(
            string name,
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            TimelineAsset timeline,
            FacialTimelineBakeAsset bake)
        {
            Profile = profile;
            Timeline = timeline;
            Derivation = TimelineChannelDeriver.Derive(TimelineAssetScanner.Scan(timeline).Tracks, profile);

            _hostObject = TimelinePlayModeControllerHost.Create(name + "_Host", profile, blendShapeNames);
            Controller = _hostObject.GetComponent<FacialController>();
            Receiver = _hostObject.AddComponent<FacialTimelineReceiver>();
            Receiver.BakeAsset = bake;

            _binding = new TimelineAdapterBinding();
            _binding.OnStart(new AdapterBuildContext(
                profile,
                blendShapeNames,
                Controller.InputSourceRegistry,
                new FacialOutputBus(),
                new NoopTimeProvider(),
                _hostObject,
                lipSyncProvider: null));

            _directorObject = new GameObject(name + "_Director");
            Director = _directorObject.AddComponent<PlayableDirector>();
            Director.playOnAwake = false;
            Director.playableAsset = timeline;
            Director.timeUpdateMode = DirectorUpdateMode.Manual;
            Director.extrapolationMode = DirectorWrapMode.None;
            BindAllFacialTracks(Director, timeline, Receiver);
        }

        public FacialProfile Profile { get; }

        public TimelineAsset Timeline { get; }

        public TimelineDerivation Derivation { get; }

        public FacialController Controller { get; }

        public FacialTimelineReceiver Receiver { get; }

        public PlayableDirector Director { get; }

        /// <summary>
        /// リグを組み、Director を Play → 時刻 0 で評価してセッションを Active にする。
        /// 導出した一致レイヤーがすべて Connector で接続されたことを診断コードで確認する。
        /// </summary>
        public static TimelinePlayModeRig CreateActive(
            string name,
            FacialProfile profile,
            IReadOnlyList<string> blendShapeNames,
            TimelineAsset timeline,
            FacialTimelineBakeAsset bake,
            Action<TimelinePlayModeRig> beforePlay = null)
        {
            var rig = new TimelinePlayModeRig(name, profile, blendShapeNames, timeline, bake);
            beforePlay?.Invoke(rig);
            rig.Director.Play();
            rig.Director.playableGraph.Evaluate(0f);

            Assert.That(rig.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), "fixture: セッションが Active");
            IReadOnlyList<TimelineLayerDescriptor> layers = rig.Derivation.Layers;
            int matched = 0;
            for (int i = 0; i < layers.Count; i++)
            {
                if (!layers[i].IsMatched)
                {
                    continue;
                }

                matched++;
                Assert.That(rig.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LayerConnected, layers[i].LayerName), Is.True,
                    $"fixture: 導出レイヤー '{layers[i].LayerName}' が Connector で接続されている");
            }

            Assert.That(matched, Is.GreaterThan(0), "fixture: トラック名から少なくとも 1 レイヤーが導出される");
            Assert.That(rig.Receiver.ConnectedLayerNames.Count, Is.EqualTo(matched));
            return rig;
        }

        public TimelineExpressionStateSink GetStateSink(string layerName)
        {
            Assert.That(Receiver.TryGetExpressionSink(layerName, out TimelineExpressionStateSink sink), Is.True,
                $"fixture: '{layerName}' の state sink");
            return sink;
        }

        public TimelineBakedValueSink GetValueSink(string layerName)
        {
            Assert.That(Receiver.TryGetExpressionValueSink(layerName, out TimelineBakedValueSink sink), Is.True,
                $"fixture: '{layerName}' の値 sink");
            return sink;
        }

        public void Dispose()
        {
            if (Director != null && Director.playableGraph.IsValid())
            {
                Director.playableGraph.Destroy();
            }

            _binding.Dispose();

            if (_directorObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_directorObject);
            }

            TimelinePlayModeControllerHost.Destroy(_hostObject);
        }

        /// <summary>同じ Receiver を全 Facial トラックに binding する（別 Director で競合を作るときにも使う）。</summary>
        public static void BindAllFacialTracks(PlayableDirector director, TimelineAsset timeline, FacialTimelineReceiver receiver)
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is Tracks.FacialExpressionTrack || track is Tracks.FacialValueTrack)
                {
                    director.SetGenericBinding(track, receiver);
                }
            }
        }

        private sealed class NoopTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => 0d;
        }
    }
}
