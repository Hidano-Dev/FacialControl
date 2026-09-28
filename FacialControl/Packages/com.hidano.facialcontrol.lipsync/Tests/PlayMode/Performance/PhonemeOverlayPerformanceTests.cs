using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public sealed class PhonemeOverlayPerformanceTests : SizedTestFixture
    {
        private const int CharacterCount = 10;
        private const int BlendShapeCount = 25;
        private const int WarmUpFrames = 64;
        private const int MeasuredFrames = 1000;
        private const double FrameDt = 1.0 / 60.0;
        private const string EmotionLayer = "emotion";
        private const string FacePath = "Face";

        private static readonly string[] BlendShapeNames = CreateBlendShapeNames();
        private static readonly string[] ReservedSlots = PhonemeOverlaySlots.ReservedNames.ToArray();

        [Test]
        public void TenCharacters_FiveSlotsEach_PerFrameZeroAllocation()
        {
            var pipelines = new CharacterPipeline[CharacterCount];
            try
            {
                for (int i = 0; i < pipelines.Length; i++)
                {
                    pipelines[i] = CharacterPipeline.CreateLipSyncOnly(i);
                }

                WarmUp(pipelines);
                ForceFullCollection();

                long gcAllocBytes = MeasureHotLoop(pipelines, switchExpressions: false);

                Assert.That(gcAllocBytes, Is.EqualTo(0L),
                    "10 characters x 5 phoneme slots aggregate hot path reported GC.Alloc: " + gcAllocBytes + " bytes.");
                AssertPipelinesProducedValues(pipelines);
            }
            finally
            {
                DisposeAll(pipelines);
            }
        }

        [Test]
        public void SingleCharacter_FiveSlotsFiveOverrides_PerFrameZeroAllocation()
        {
            var pipelines = new[] { CharacterPipeline.CreateOverlayOverrides(characterIndex: 0) };
            try
            {
                WarmUp(pipelines);
                ForceFullCollection();

                long gcAllocBytes = MeasureHotLoop(pipelines, switchExpressions: false);

                Assert.That(gcAllocBytes, Is.EqualTo(0L),
                    "single character x 5 phoneme override slots aggregate hot path reported GC.Alloc: " + gcAllocBytes + " bytes.");
                AssertPipelinesProducedValues(pipelines);
            }
            finally
            {
                DisposeAll(pipelines);
            }
        }

        [Test]
        public void ExpressionSwitch_PerFrameZeroAllocation()
        {
            var pipelines = new[] { CharacterPipeline.CreateSwitchingOverrides(characterIndex: 0) };
            try
            {
                WarmUp(pipelines);
                ForceFullCollection();

                long gcAllocBytes = MeasureHotLoop(pipelines, switchExpressions: true);

                Assert.That(gcAllocBytes, Is.EqualTo(0L),
                    "expression switch phoneme overlay aggregate hot path reported GC.Alloc: " + gcAllocBytes + " bytes.");
                AssertPipelinesProducedValues(pipelines);
            }
            finally
            {
                DisposeAll(pipelines);
            }
        }

        private static long MeasureHotLoop(CharacterPipeline[] pipelines, bool switchExpressions)
        {
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int frame = 0; frame < MeasuredFrames; frame++)
            {
                RunFrame(pipelines, frame, switchExpressions);
            }

            return recorder.LastValue;
        }

        private static void WarmUp(CharacterPipeline[] pipelines)
        {
            for (int frame = 0; frame < WarmUpFrames; frame++)
            {
                RunFrame(pipelines, frame, switchExpressions: true);
            }
        }

        private static void RunFrame(CharacterPipeline[] pipelines, int frame, bool switchExpressions)
        {
            for (int i = 0; i < pipelines.Length; i++)
            {
                CharacterPipeline pipeline = pipelines[i];
                pipeline.Time.UnscaledTimeSeconds += FrameDt;

                if (switchExpressions)
                {
                    pipeline.ActiveProvider.Active = (frame & 1) == 0
                        ? pipeline.PrimaryExpression
                        : pipeline.SecondaryExpression;
                }

                pipeline.Aggregator.Aggregate(
                    deltaTime: (float)FrameDt,
                    pipeline.Priorities,
                    pipeline.LayerWeights,
                    pipeline.OutputPerLayer);
            }
        }

        private static void AssertPipelinesProducedValues(CharacterPipeline[] pipelines)
        {
            for (int i = 0; i < pipelines.Length; i++)
            {
                ReadOnlySpan<float> values = pipelines[i].OutputPerLayer[0].BlendShapeValues.Span;
                bool hasOutput = false;
                for (int k = 0; k < values.Length; k++)
                {
                    if (values[k] > 0f)
                    {
                        hasOutput = true;
                        break;
                    }
                }

                Assert.That(hasOutput, Is.True, "pipeline " + i + " did not produce phoneme overlay output.");
            }
        }

        private static void DisposeAll(CharacterPipeline[] pipelines)
        {
            for (int i = 0; i < pipelines.Length; i++)
            {
                pipelines[i]?.Dispose();
            }
        }

        private static void ForceFullCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static FacialProfile BuildProfile(params Expression[] expressions)
        {
            return new FacialProfile(
                schemaVersion: "1.0",
                layers: new[] { new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins) },
                expressions: expressions,
                rendererPaths: null,
                layerInputSources: null,
                defaultOverlays: null,
                slots: ReservedSlots);
        }

        private static Expression CreateExpression(string id, float baseWeight)
        {
            var overlays = new OverlaySlotBinding[ReservedSlots.Length];
            for (int i = 0; i < ReservedSlots.Length; i++)
            {
                overlays[i] = new OverlaySlotBinding(
                    ReservedSlots[i],
                    suppress: false,
                    snapshot: CreateSnapshot(id + "-" + ReservedSlots[i], i, baseWeight));
            }

            return new Expression(
                id: id,
                name: id,
                layer: EmotionLayer,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurve: default,
                blendShapeValues: null,
                overlays: overlays);
        }

        private static ExpressionSnapshot CreateSnapshot(string id, int slotIndex, float baseWeight)
        {
            return new ExpressionSnapshot(
                id: id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[]
                {
                    new BlendShapeSnapshot(FacePath, BlendShapeNames[slotIndex], baseWeight + (slotIndex * 0.05f)),
                },
                bones: null,
                rendererPaths: new[] { FacePath });
        }

        private static PhonemeSnapshot[] CreatePhonemeSnapshots(int characterIndex)
        {
            var snapshots = new PhonemeSnapshot[ReservedSlots.Length];
            for (int i = 0; i < ReservedSlots.Length; i++)
            {
                var weights = new float[BlendShapeCount];
                weights[i] = 0.35f + (characterIndex * 0.01f);
                snapshots[i] = new PhonemeSnapshot(
                    PhonemeOverlaySlots.MapReservedToPhonemeId(ReservedSlots[i]),
                    weights);
            }

            return snapshots;
        }

        private static Dictionary<string, float> CreateRatios()
        {
            var ratios = new Dictionary<string, float>(ReservedSlots.Length, StringComparer.Ordinal);
            for (int i = 0; i < ReservedSlots.Length; i++)
            {
                ratios[PhonemeOverlaySlots.MapReservedToPhonemeId(ReservedSlots[i])] = 0.2f;
            }

            return ratios;
        }

        private static void ApplyRatios(FakePhonemeWeightSource source, Dictionary<string, float> ratios)
        {
            source.CurrentVolume = 1f;
            foreach (KeyValuePair<string, float> ratio in ratios)
            {
                source.SetPhonemeWeight(ratio.Key, ratio.Value);
            }
        }

        private static string[] CreateBlendShapeNames()
        {
            var names = new string[BlendShapeCount];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = "Mouth_" + i;
            }

            return names;
        }

        private sealed class CharacterPipeline : IDisposable
        {
            private readonly ULipSyncProvider _provider;
            private readonly LayerInputSourceRegistry _registry;
            private readonly LayerInputSourceWeightBuffer _weightBuffer;

            private CharacterPipeline(
                FacialProfile profile,
                StubActiveExpressionProvider activeProvider,
                Expression primaryExpression,
                Expression secondaryExpression,
                List<(int, int, IInputSource)> bindings,
                ULipSyncProvider provider,
                ManualTimeProvider time)
            {
                ActiveProvider = activeProvider;
                PrimaryExpression = primaryExpression;
                SecondaryExpression = secondaryExpression;
                _provider = provider;
                Time = time;

                _registry = new LayerInputSourceRegistry(profile, BlendShapeCount, bindings);
                _weightBuffer = new LayerInputSourceWeightBuffer(
                    _registry.LayerCount,
                    _registry.MaxSourcesPerLayer);
                for (int i = 0; i < bindings.Count; i++)
                {
                    _weightBuffer.SetWeight(0, i, 1f);
                }

                Aggregator = new LayerInputSourceAggregator(_registry, _weightBuffer, BlendShapeCount, time);
                Priorities = new[] { 0 };
                LayerWeights = new[] { 1f };
                OutputPerLayer = new LayerBlender.LayerInput[1];
            }

            public StubActiveExpressionProvider ActiveProvider { get; }
            public Expression PrimaryExpression { get; }
            public Expression SecondaryExpression { get; }
            public ManualTimeProvider Time { get; }
            public LayerInputSourceAggregator Aggregator { get; }
            public int[] Priorities { get; }
            public float[] LayerWeights { get; }
            public LayerBlender.LayerInput[] OutputPerLayer { get; }

            public static CharacterPipeline CreateLipSyncOnly(int characterIndex)
            {
                var primary = CreateExpression("primary-" + characterIndex, 0.50f);
                var secondary = CreateExpression("secondary-" + characterIndex, 0.25f);
                var profile = BuildProfile(primary, secondary);
                var activeProvider = new StubActiveExpressionProvider();
                var time = new ManualTimeProvider();
                var weightSource = new FakePhonemeWeightSource();
                var provider = new ULipSyncProvider(
                    weightSource,
                    CreatePhonemeSnapshots(characterIndex),
                    BlendShapeCount);
                ApplyRatios(weightSource, CreateRatios());

                var bindings = new List<(int, int, IInputSource)>(ReservedSlots.Length);
                for (int i = 0; i < ReservedSlots.Length; i++)
                {
                    string slot = ReservedSlots[i];
                    bindings.Add((
                        0,
                        i,
                        new LipSyncPhonemeOverlayInputSource(
                            InputSourceId.Parse("lipsync-overlay:" + characterIndex + ":" + slot),
                            PhonemeOverlaySlots.MapReservedToPhonemeId(slot),
                            provider,
                            BlendShapeCount)));
                }

                return new CharacterPipeline(profile, activeProvider, primary, secondary, bindings, provider, time);
            }

            public static CharacterPipeline CreateOverlayOverrides(int characterIndex)
            {
                var primary = CreateExpression("primary-" + characterIndex, 0.50f);
                var secondary = CreateExpression("secondary-" + characterIndex, 0.25f);
                var activeProvider = new StubActiveExpressionProvider { Active = primary };
                var profile = BuildProfile(primary, secondary);
                return CreateOverlayPipeline(characterIndex, profile, activeProvider, primary, secondary);
            }

            public static CharacterPipeline CreateSwitchingOverrides(int characterIndex)
            {
                var primary = CreateExpression("primary-" + characterIndex, 0.50f);
                var secondary = CreateExpression("secondary-" + characterIndex, 0.25f);
                var activeProvider = new StubActiveExpressionProvider { Active = primary };
                var profile = BuildProfile(primary, secondary);
                return CreateOverlayPipeline(characterIndex, profile, activeProvider, primary, secondary);
            }

            public void Dispose()
            {
                _provider.Dispose();
                _weightBuffer.Dispose();
                _registry.Dispose();
            }

            // Override 解決付きの LipSyncPhonemeOverlayInputSource で構成する。
            // Override は駆動 weight（音素 weight × 音量）で差し替え出力されるため、
            // FakePhonemeWeightSource へ ratios を流さないと出力が出ない。
            private static CharacterPipeline CreateOverlayPipeline(
                int characterIndex,
                FacialProfile profile,
                StubActiveExpressionProvider activeProvider,
                Expression primary,
                Expression secondary)
            {
                var time = new ManualTimeProvider();
                var weightSource = new FakePhonemeWeightSource();
                var provider = new ULipSyncProvider(
                    weightSource,
                    CreatePhonemeSnapshots(characterIndex),
                    BlendShapeCount);
                ApplyRatios(weightSource, CreateRatios());

                var bindings = new List<(int, int, IInputSource)>(ReservedSlots.Length);
                for (int i = 0; i < ReservedSlots.Length; i++)
                {
                    string slot = ReservedSlots[i];
                    bindings.Add((
                        0,
                        i,
                        new LipSyncPhonemeOverlayInputSource(
                            InputSourceId.Parse("lipsync-overlay:" + characterIndex + ":" + slot),
                            PhonemeOverlaySlots.MapReservedToPhonemeId(slot),
                            provider,
                            BlendShapeCount,
                            slot,
                            BlendShapeNames,
                            profile,
                            activeProvider,
                            EmotionLayer)));
                }

                return new CharacterPipeline(profile, activeProvider, primary, secondary, bindings, provider, time);
            }
        }

        private sealed class StubActiveExpressionProvider : IActiveExpressionProvider
        {
            public Expression? Active;

            public Expression? TryGetTopActiveExpression(string layerName)
            {
                return Active;
            }
        }

        private sealed class ManualTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds { get; set; }
        }
    }
}
