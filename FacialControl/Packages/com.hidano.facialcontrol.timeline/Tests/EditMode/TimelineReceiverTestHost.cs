using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Receiver のセッションを EditMode で組むためのテスト用ホスト。
    /// FacialController（Animator + BlendShape 付きメッシュ）・Receiver・Director を 1 つの GameObject に置き、
    /// 実 <see cref="InputSourceRegistry"/> を接続コンテキストとして渡せるようにする。
    /// </summary>
    /// <remarks>
    /// EditMode では FacialController の child scope（registry）は作られないが、後付け接続 API は使える。
    /// registry は本ホストが用意したものを接続コンテキストで Receiver に渡す。
    /// </remarks>
    internal sealed class TimelineReceiverTestHost : IDisposable
    {
        public static readonly AdapterSlug TimelineSlug = AdapterSlug.Parse("timeline");

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        private TimelineReceiverTestHost()
        {
        }

        public GameObject Root { get; private set; }
        public FacialController Controller { get; private set; }
        public FacialTimelineReceiver Receiver { get; private set; }
        public PlayableDirector Director { get; private set; }
        public InputSourceRegistry Registry { get; private set; }
        public FacialProfile Profile { get; private set; }
        public string[] BlendShapeNames { get; private set; }

        /// <param name="profile">controller を初期化する Profile。</param>
        /// <param name="blendShapeNames">ホストメッシュの BlendShape 名。</param>
        /// <param name="initializeController">false なら controller を未初期化のまま残す。</param>
        public static TimelineReceiverTestHost Create(
            FacialProfile profile,
            string[] blendShapeNames,
            bool initializeController = true)
        {
            FacialControllerRendererOwnership.Clear();
            var host = new TimelineReceiverTestHost
            {
                Profile = profile,
                BlendShapeNames = blendShapeNames,
                Registry = new InputSourceRegistry(),
            };

            host.Root = new GameObject("TimelineReceiverTestHost");
            host._created.Add(host.Root);
            host.Root.AddComponent<Animator>();

            var face = new GameObject("Face");
            face.transform.SetParent(host.Root.transform, false);
            var renderer = face.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = host.CreateMesh(blendShapeNames);

            host.Controller = host.Root.AddComponent<FacialController>();
            host.Receiver = host.Root.AddComponent<FacialTimelineReceiver>();
            host.Director = host.Root.AddComponent<PlayableDirector>();

            if (initializeController)
            {
                host.InitializeController();
            }

            return host;
        }

        public void InitializeController()
        {
            Controller.InitializeWithProfile(Profile);
            if (!Controller.IsInitialized)
            {
                throw new InvalidOperationException("fixture: FacialController could not be initialized.");
            }
        }

        public TimelineBindingContext Context(bool enabled = true, FacialController controller = null)
        {
            return new TimelineBindingContext(
                TimelineSlug,
                Profile,
                BlendShapeNames,
                Registry,
                controller != null ? controller : Controller,
                enabled);
        }

        public void Attach(bool enabled = true)
        {
            Receiver.AttachBinding(Context(enabled));
        }

        /// <summary>
        /// Timeline binding を AdapterBindings に持つ Profile SO を controller に設定する（静的診断の ProfileBinding 領域を満たす）。
        /// Bake のハッシュは SO の GazeChannels を含めて焼き直す必要があるため、<see cref="StampHashes"/> より前に呼ぶ。
        /// </summary>
        public TimelineTestProfileSO AssignProfileSource()
        {
            var so = ScriptableObject.CreateInstance<TimelineTestProfileSO>();
            _created.Add(so);
            so.WritableAdapterBindings.Add(new Hidano.FacialControl.Timeline.Adapters.AdapterBindings.TimelineAdapterBinding());
            Controller.CharacterSO = so;
            return so;
        }

        public T Track<T>(UnityEngine.Object obj) where T : UnityEngine.Object
        {
            _created.Add(obj);
            return (T)obj;
        }

        public TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            return timeline;
        }

        public FacialTimelineBakeAsset CreateBake(
            TimelineAsset timeline,
            params (string layer, string blendShape, float value)[] curves)
        {
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            _created.Add(bake);
            var bakes = new List<ExpressionSourceBake>();
            for (int i = 0; i < curves.Length; i++)
            {
                ExpressionSourceBake target = bakes.Find(b => b.LayerName == curves[i].layer);
                if (target == null)
                {
                    target = new ExpressionSourceBake { LayerName = curves[i].layer };
                    bakes.Add(target);
                }

                var list = new List<BlendShapeCurve>(target.Curves)
                {
                    new BlendShapeCurve
                    {
                        BlendShapeName = curves[i].blendShape,
                        Curve = AnimationCurve.Constant(0f, 10f, curves[i].value),
                    },
                };
                target.Curves = list.ToArray();
            }

            bake.ExpressionBakes = bakes.ToArray();
            StampHashes(timeline, bake);
            return bake;
        }

        /// <summary>現在の Timeline / Profile から Bake のハッシュを焼き直す（Fresh + ProfileMatched の状態にする）。</summary>
        public void StampHashes(TimelineAsset timeline, FacialTimelineBakeAsset bake)
        {
            GazeChannel[] gaze = FacialTimelineHashCalculator.ToGazeChannelArray(
                Controller.CharacterSO != null ? Controller.CharacterSO.GazeChannels : null);
            bake.ProfileContentHashHex = FacialTimelineHashCalculator.ComputeProfileContentHashHex(Profile, gaze);
            bake.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(timeline, Profile, gaze, bake.SampleRate);
        }

        /// <summary>全 Facial トラック（root + 子）に同じ Bake 参照を書く。</summary>
        public static void AssignBakeToAllTracks(TimelineAsset timeline, FacialTimelineBakeAsset bake)
        {
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                ((IFacialTimelineBakeHolder)tracks[i]).Bake = bake;
            }
        }

        public void BindDirector(TimelineAsset timeline)
        {
            Director.playableAsset = timeline;
            Director.timeUpdateMode = DirectorUpdateMode.Manual;
            Director.extrapolationMode = DirectorWrapMode.None;
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (tracks[i].parent is TimelineAsset)
                {
                    Director.SetGenericBinding(tracks[i], Receiver);
                }
            }
        }

        public string[] SnapshotRegistryIds()
        {
            IReadOnlyList<string> ids = Registry.RegisteredIds;
            var copy = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                copy[i] = ids[i];
            }

            Array.Sort(copy, StringComparer.Ordinal);
            return copy;
        }

        public void Dispose()
        {
            if (Director != null && Director.playableGraph.IsValid())
            {
                Director.playableGraph.Destroy();
            }

            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
            FacialControllerRendererOwnership.Clear();
        }

        private Mesh CreateMesh(string[] blendShapeNames)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, new Vector3[3], null, null);
            }

            _created.Add(mesh);
            return mesh;
        }
    }

    /// <summary>AdapterBindings をテストから書き換えられる Profile SO。</summary>
    internal sealed class TimelineTestProfileSO : Hidano.FacialControl.Adapters.ScriptableObject.Serializable.FacialCharacterProfileSO
    {
        public List<Hidano.FacialControl.Domain.Adapters.AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
    }

    /// <summary>注入型ではない Analog 入力源（registry の原本役）。値を外から設定できる。</summary>
    internal sealed class TestAnalogSource : IInputSource, IAnalogInputSource
    {
        private readonly float[] _axes;

        public TestAnalogSource(string id, int axisCount, float value = 0f)
        {
            Id = id;
            _axes = new float[axisCount];
            for (int i = 0; i < _axes.Length; i++)
            {
                _axes[i] = value;
            }
        }

        public string Id { get; }
        public InputSourceType Type => InputSourceType.ValueProvider;
        public int BlendShapeCount => 0;
        public BitArray ContributeMask { get; } = new BitArray(0);
        public bool IsValid => true;
        public int AxisCount => _axes.Length;

        public void Tick(float deltaTime)
        {
        }

        public bool TryWriteValues(Span<float> output)
        {
            return false;
        }

        public bool TryReadScalar(out float value)
        {
            value = _axes.Length > 0 ? _axes[0] : 0f;
            return _axes.Length > 0;
        }

        public bool TryReadVector2(out float x, out float y)
        {
            x = _axes.Length > 0 ? _axes[0] : 0f;
            y = _axes.Length > 1 ? _axes[1] : 0f;
            return _axes.Length > 1;
        }

        public bool TryReadAxes(Span<float> output)
        {
            int count = Math.Min(output.Length, _axes.Length);
            for (int i = 0; i < count; i++)
            {
                output[i] = _axes[i];
            }

            return count > 0;
        }
    }

    /// <summary>Console 出力を種別ごとに数える。失敗扱いのログ（Error）を許容するため ignoreFailingMessages を立てる。</summary>
    internal sealed class TimelineLogCounter : IDisposable
    {
        private readonly bool _previousIgnore;

        public TimelineLogCounter()
        {
            _previousIgnore = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            UnityEngine.Application.logMessageReceived += OnLog;
        }

        public int Errors { get; private set; }
        public int Warnings { get; private set; }

        public void Dispose()
        {
            UnityEngine.Application.logMessageReceived -= OnLog;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = _previousIgnore;
        }

        private void OnLog(string condition, string stackTrace, LogType type)
        {
            if (condition == null || !condition.Contains("FacialTimelineReceiver"))
            {
                return;
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                Errors++;
            }
            else if (type == LogType.Warning)
            {
                Warnings++;
            }
        }
    }
}
