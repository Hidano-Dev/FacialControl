using System;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Tests.Shared;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tests.Shared;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// Timeline 再生のセッション開始後の毎フレーム経路（Mixer の ProcessFrame / SampleExpressionValues / Aggregate）が
    /// GC 確保しないことを固定する。
    /// </summary>
    /// <remarks>
    /// 計測は Memory カウンタ「GC Allocated In Frame」の <c>CurrentValue</c> 差分（<see cref="ManagedAllocationProbe"/>）で、
    /// 同期 [Test] の 1 フレーム内に閉じる。旧実装の <c>GC.Alloc</c> マーカーの <c>LastValue</c> は
    /// 完了済みフレームの値しか返さず、同期テストの窓内の確保を検出できなかった（空振りの 0 ゲート）。
    /// 計測器が確保を検出できることは <see cref="AllocationProbe_DeliberateAllocationInWindow_IsDetected"/> で自己検証する。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class TimelineGcZeroGateTests : SizedTestFixture
    {
        private const int WarmupFrames = 8;
        private const int MeasurementFrames = 120;
        private const float FrameDeltaTime = 1f / 60f;
        private const string ExpressionLayerName = "Expressions";
        private const string ExpressionId = "smile";
        private static readonly string[] BlendShapeNames = { "Smile" };
        private const string ValueProviderId = "ifm";

        private static object s_allocationSink;

        [Test]
        public void AllocationProbe_DeliberateAllocationInWindow_IsDetected()
        {
            // 計測器の自己検証（positive control）: 窓内で意図的に確保すれば 0 にならないこと（小さな確保も含む）。
            long large = ManagedAllocationProbe.MeasureAllocatedBytes(
                () => s_allocationSink = new byte[256], iterations: MeasurementFrames, warmupIterations: 0);
            long small = ManagedAllocationProbe.MeasureAllocatedBytes(
                () => s_allocationSink = new object(), iterations: 1, warmupIterations: 0);
            s_allocationSink = null;

            Assert.That(large, Is.GreaterThanOrEqualTo(256L * MeasurementFrames),
                "計測器が窓内の確保を検出できない（空振りの 0 ゲートになっている）。");
            Assert.That(small, Is.GreaterThan(0L), "1 オブジェクトの小さな確保も検出できること");
        }

        [Test]
        public void TimelinePlayback_SteadyState_AfterWarmup_AllocatesZeroGC()
        {
            using var fixture = new TimelinePlaybackFixture();

            long allocated = MeasureFrames(() => fixture.AdvanceLinearly(FrameDeltaTime));

            Assert.That(allocated, Is.EqualTo(0L),
                "Timeline steady-state playback hot path must not allocate GC.");
        }

        [Test]
        public void TimelinePlayback_WithValueProviderTrack_SteadyState_AllocatesZeroGC()
        {
            using var fixture = new TimelinePlaybackFixture(withValueProviderTrack: true);
            Assert.That(
                fixture.Rig.Receiver.TryGetValueProviderSink(ValueProviderId, out TimelineValueProviderInputSource sink),
                Is.True,
                "fixture: 値提供型トラックが乗っ取りを済ませている");

            long allocated = MeasureFrames(() => fixture.AdvanceLinearly(FrameDeltaTime));

            Assert.That(sink.IsValid, Is.True, "fixture: 計測中も値提供型の値を書いている");
            Assert.That(allocated, Is.EqualTo(0L),
                "Timeline playback with a value-provider track must not allocate GC.");
        }

        [Test]
        public void TimelinePlayback_ScrubJump_AfterWarmup_AllocatesZeroGC()
        {
            using var fixture = new TimelinePlaybackFixture();

            fixture.AdvanceLinearly(1.25f);
            fixture.JumpTo(0.25d);
            fixture.JumpTo(1.25d);

            int frame = 0;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(
                () => fixture.JumpTo((frame++ & 1) == 0 ? 0.25d : 1.25d),
                iterations: MeasurementFrames,
                warmupIterations: WarmupFrames);

            Assert.That(allocated, Is.EqualTo(0L),
                "Timeline jump evaluation must remain zero-alloc after scratch buffers are warmed.");
        }

        [Test]
        public void TimelinePlayback_PauseResume_ReusesSessionResourcesAndStaysZeroAlloc()
        {
            using var fixture = new TimelinePlaybackFixture();
            fixture.AdvanceLinearly(FrameDeltaTime);
            TimelineBakedValueSink valueSinkBefore = fixture.Rig.GetValueSink(ExpressionLayerName);
            TimelineExpressionStateSink stateSinkBefore = fixture.Rig.GetStateSink(ExpressionLayerName);

            // Pause 相当: Mixer の OnBehaviourPause / OnGraphStop が呼ぶ ReleaseAll でセッションは Idle に戻る
            // （Manual 更新の Director では Pause() が同期的に OnBehaviourPause を発火しないため直接呼ぶ）。
            fixture.Rig.Receiver.ReleaseAll();
            Assert.That(fixture.Rig.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle), "ReleaseAll で Idle に戻る");

            // Resume: 次の ProcessFrame で再 Begin。セッション資源（sink）はプールから再利用され作り直されない。
            fixture.AdvanceLinearly(FrameDeltaTime);
            Assert.That(fixture.Rig.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(fixture.Rig.GetValueSink(ExpressionLayerName), Is.SameAs(valueSinkBefore), "値 sink を再確保しない");
            Assert.That(fixture.Rig.GetStateSink(ExpressionLayerName), Is.SameAs(stateSinkBefore), "state sink を再確保しない");
            Assert.That(fixture.Rig.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LayerConnected, ExpressionLayerName), Is.True);

            long allocated = MeasureFrames(() => fixture.AdvanceLinearly(FrameDeltaTime));

            Assert.That(allocated, Is.EqualTo(0L), "Resume 後の ProcessFrame も確保しない。");
        }

        [Test]
        public void TimelinePlayback_SessionConflictWithSecondDirector_AfterWarmup_AllocatesZeroGC()
        {
            // 6.1 レビュー F1: 所有者でない Director の Mixer が毎フレーム Begin を呼んでも、記録済みの競合では確保しない。
            // 競合は Error として Console に 1 回出る（判定は文言ではなく診断コードで行う）。
            bool previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                using var fixture = new TimelinePlaybackFixture(withConflictingDirector: true);
                fixture.AdvanceLinearly(FrameDeltaTime);

                Assert.That(fixture.Rig.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.SessionConflict),
                    Is.True, "fixture: 2 つ目の Director が競合として記録されている");
                Assert.That(fixture.Rig.Receiver.IsSessionOwnedBy(fixture.Rig.Director), Is.True, "所有者は最初の Director のまま");

                long allocated = MeasureFrames(() => fixture.AdvanceLinearly(FrameDeltaTime));

                Assert.That(allocated, Is.EqualTo(0L), "SessionConflict 中の ProcessFrame も確保しない。");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        [Test]
        public void Aggregator_WithoutObserver_AfterWarmup_AllocatesZeroGC()
        {
            FacialProfile profile = CreateProfile();
            using var sink = new TimelineBakedValueAggregationFixture(profile);

            int frame = 0;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(
                () => sink.AggregateFrame(0.25f + ((frame++ & 7) * 0.02f)),
                iterations: MeasurementFrames,
                warmupIterations: WarmupFrames);

            Assert.That(allocated, Is.EqualTo(0L),
                "LayerInputSourceAggregator must stay zero-alloc when no observer is attached.");
        }

        private static long MeasureFrames(Action frame)
        {
            return ManagedAllocationProbe.MeasureAllocatedBytes(frame, iterations: MeasurementFrames, warmupIterations: WarmupFrames);
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition(ExpressionLayerName, 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: ExpressionId,
                        name: "Smile",
                        layer: ExpressionLayerName,
                        transitionDuration: 0.10f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }

        /// <summary>BlendShape "Smile" を値ごと・寄与 mask・有効状態の階段カーブで動かす値提供型トラックを足す。</summary>
        private static void AddValueProviderTrack(TimelineAsset timeline)
        {
            var track = timeline.CreateTrack<FacialValueTrack>(null, ValueProviderId);
            track.ChannelSubId = ValueProviderId;
            track.ChannelKind = FacialValueChannelKind.ValueProvider;
            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0d;
            clip.duration = 10d;
            var keys = new Keyframe[60];
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i] = new Keyframe(i / 30f, (i % 10) / 10f, float.PositiveInfinity, float.PositiveInfinity);
            }

            var valueClip = (FacialValueClip)clip.asset;
            valueClip.Axes = new[] { new AnimationCurve(keys) };
            valueClip.BlendShapeNames = new[] { BlendShapeNames[0] };
            valueClip.BlendShapeIndices = new[] { 0 };
            valueClip.Contributes = new[] { new AnimationCurve(new Keyframe(0f, 1f, float.PositiveInfinity, float.PositiveInfinity)) };
            valueClip.Validity = new AnimationCurve(new Keyframe(0f, 1f, float.PositiveInfinity, float.PositiveInfinity));
        }

        private static TimelineAsset CreateTimeline()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, ExpressionLayerName);
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 2d;
            ((FacialExpressionClip)clip.asset).ExpressionId = ExpressionId;
            return timeline;
        }

        /// <summary>
        /// 再生リグ（Receiver の自動導出 + Connector 接続）に、値 sink を読む Aggregator を足して 1 フレーム分の合成まで回す。
        /// </summary>
        private sealed class TimelinePlaybackFixture : IDisposable
        {
            private readonly TimelineAsset _timeline;
            private readonly FacialTimelineBakeAsset _bake;
            private readonly TimelineBakedValueSink _valueSink;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities = { 0 };
            private readonly float[] _layerWeights = { 1f };
            private readonly float[] _output = new float[1];
            private readonly GameObject _conflictObject;
            private readonly PlayableDirector _conflictDirector;

            public TimelinePlaybackFixture(bool withConflictingDirector = false, bool withValueProviderTrack = false)
            {
                FacialProfile profile = CreateProfile();
                _timeline = CreateTimeline();
                if (withValueProviderTrack)
                {
                    AddValueProviderTrack(_timeline);
                }

                _bake = TimelineBakeService.Bake(_timeline, profile);
                Rig = TimelinePlayModeRig.CreateActive(
                    "TimelineGcZeroGateTests",
                    profile,
                    BlendShapeNames,
                    _timeline,
                    _bake,
                    withValueProviderTrack
                        ? rig => rig.Controller.InputSourceRegistry.Register(
                            AdapterSlug.Parse(ValueProviderId),
                            new FakeLiveValueProvider(InputSourceId.Parse(ValueProviderId), BlendShapeNames.Length))
                        : (Action<TimelinePlayModeRig>)null);
                _valueSink = Rig.GetValueSink(ExpressionLayerName);

                if (withConflictingDirector)
                {
                    // 同じ Receiver を binding する 2 つ目の Director（所有者ではないので SessionConflict になる）。
                    _conflictObject = new GameObject("TimelineGcZeroGateTests_ConflictDirector");
                    _conflictDirector = _conflictObject.AddComponent<PlayableDirector>();
                    _conflictDirector.playOnAwake = false;
                    _conflictDirector.playableAsset = _timeline;
                    _conflictDirector.timeUpdateMode = DirectorUpdateMode.Manual;
                    _conflictDirector.extrapolationMode = DirectorWrapMode.None;
                    TimelinePlayModeRig.BindAllFacialTracks(_conflictDirector, _timeline, Rig.Receiver);
                    _conflictDirector.Play();
                    _conflictDirector.playableGraph.Evaluate(0f);
                }

                _registry = new LayerInputSourceRegistry(
                    profile,
                    blendShapeCount: 1,
                    new[] { (0, 0, (IInputSource)_valueSink) });
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                _weightBuffer.SetWeight(0, 0, 1f);
                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount: 1);

                AggregateCurrentValues();
            }

            public TimelinePlayModeRig Rig { get; }

            public void AdvanceLinearly(float deltaTime)
            {
                Rig.Director.playableGraph.Evaluate(deltaTime);
                if (_conflictDirector != null)
                {
                    _conflictDirector.playableGraph.Evaluate(deltaTime);
                }

                AggregateCurrentValues();
            }

            public void JumpTo(double targetTime)
            {
                Rig.Director.time = targetTime;
                Rig.Director.Evaluate();
                AggregateCurrentValues();
            }

            public void Dispose()
            {
                if (_conflictDirector != null && _conflictDirector.playableGraph.IsValid())
                {
                    _conflictDirector.playableGraph.Destroy();
                }

                if (_conflictObject != null)
                {
                    UnityEngine.Object.DestroyImmediate(_conflictObject);
                }

                _registry.Dispose();
                _weightBuffer.Dispose();
                Rig.Dispose();

                if (_bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(_bake);
                }

                if (_timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(_timeline);
                }
            }

            private void AggregateCurrentValues()
            {
                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, _output);
            }
        }

        private sealed class TimelineBakedValueAggregationFixture : IDisposable
        {
            private readonly TimelineBakedValueSink _valueSink;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;
            private readonly LayerInputSourceAggregator _aggregator;
            private readonly int[] _priorities = { 0 };
            private readonly float[] _layerWeights = { 1f };
            private readonly float[] _output = new float[1];

            public TimelineBakedValueAggregationFixture(FacialProfile profile)
            {
                _valueSink = new TimelineBakedValueSink(
                    InputSourceId.Parse("timeline:bake"),
                    BlendShapeNames,
                    BlendShapeNames);
                _registry = new LayerInputSourceRegistry(
                    profile,
                    blendShapeCount: 1,
                    new[] { (0, 0, (IInputSource)_valueSink) });
                _weightBuffer = new LayerInputSourceWeightBuffer(_registry.LayerCount, _registry.MaxSourcesPerLayer);
                _weightBuffer.SetWeight(0, 0, 1f);
                _aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, blendShapeCount: 1);
            }

            public void AggregateFrame(float value)
            {
                // Assert.That は constraint 生成で確保するため、計測窓の中では使わない。
                if (!_valueSink.SetValue(0, value))
                {
                    throw new InvalidOperationException("SetValue failed.");
                }

                _aggregator.AggregateAndBlend(0f, _priorities, _layerWeights, _output);
            }

            public void Dispose()
            {
                _registry.Dispose();
                _weightBuffer.Dispose();
            }
        }
    }
}
