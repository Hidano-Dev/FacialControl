using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using NUnit.Framework;
using Unity.Profiling;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.PlayMode
{
    [TestFixture]
    [MediumTest]
    public sealed class RecGcZeroGateTests : SizedTestFixture
    {
        private const int WarmupFrames = 8;
        private const int MeasurementFrames = 120;
        private const float DeltaTime = 1f / 60f;
        private const int LargeBlendShapeCount = 300;

        [Test]
        public void RecordingUseCase_SteadyState_AfterWarmup_AllocatesZeroGC()
        {
            var observationBus = new FacialInputObservationBus();
            var clock = new ManualRecClock();
            var sink = new NullRecEventSink();
            using var useCase = new RecordingUseCase(observationBus, clock, sink);

            RecBaselineState baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0f, 0f }),
                });

            useCase.StartRecording(baseline);

            float[] axes = { 0.25f, -0.5f };
            for (int i = 0; i < WarmupFrames; i++)
            {
                PublishRecordingFrame(useCase, clock, axes, i);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                PublishRecordingFrame(useCase, clock, axes, WarmupFrames + i);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "RecordingUseCase steady-state hot path must not allocate GC.");

            useCase.StopRecording();
        }

        [Test]
        public void RecordingUseCase_LargeValueProviderSteadyState_AfterWarmup_AllocatesZeroGC()
        {
            var observationBus = new FacialInputObservationBus();
            var clock = new ManualRecClock();
            var sink = new NullRecEventSink();
            using var useCase = new RecordingUseCase(observationBus, clock, sink);
            float[] values = CreateLargeValues();
            var mask = CreateFullMask();

            RecBaselineState baseline = new RecBaselineState(
                null,
                null,
                new[]
                {
                    new RecBaselineState.ValueProviderEntry(
                        "input:vector",
                        true,
                        CreateMaskBytes(),
                        values),
                },
                null);

            useCase.StartRecording(baseline, LargeBlendShapeCount);

            for (int i = 0; i < WarmupFrames; i++)
            {
                PublishLargeValueProviderFrame(observationBus, clock, values, mask, i);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                values[0] = i;
                PublishLargeValueProviderFrame(observationBus, clock, values, mask, WarmupFrames + i);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "RecordingUseCase large value-provider steady-state hot path must not allocate GC.");

            useCase.StopRecording();
        }

        [Test]
        public void RecordingUseCase_WeightSteadyState_AfterWarmup_AllocatesZeroGC()
        {
            var observationBus = new FacialInputObservationBus();
            var clock = new ManualRecClock();
            var sink = new NullRecEventSink();
            using var useCase = new RecordingUseCase(observationBus, clock, sink);

            // 全レイヤー weight と全スロット weight を毎フレーム変化させる（Req 9.5）。
            // 基準に無いレイヤー / スロットは最初の 1 フレームで IdDefine が追記され、以後は定常状態になる。
            string[] layers = { "emotion", "lipsync", "eyes" };
            string[] slots = { "@expression", "input:osc", "input:gamepad" };
            RecBaselineState baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new Hidano.FacialControl.Domain.Models.LayerWeightEntry(layers[0], 1f) },
                new[] { new Hidano.FacialControl.Domain.Models.InputSourceWeightEntry(layers[0], slots[0], 1f) });

            useCase.StartRecording(baseline);

            for (int i = 0; i < WarmupFrames; i++)
            {
                PublishWeightFrame(useCase, clock, layers, slots, i);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                PublishWeightFrame(useCase, clock, layers, slots, WarmupFrames + i);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "RecordingUseCase weight steady-state hot path must not allocate GC.");

            useCase.StopRecording();
        }

        [Test]
        public void PlaybackUseCase_SteadyState_AfterWarmup_AllocatesZeroGC()
        {
            var triggerPort = new NullTriggerInjectionPort();
            var analogPort = new NullAnalogInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, analogPort);
            RecTimeline timeline = CreatePlaybackTimeline(WarmupFrames + MeasurementFrames + 1);

            useCase.Load(timeline, CreateProfile());
            Assert.That(useCase.StartPlayback(), Is.True);

            for (int i = 0; i < WarmupFrames; i++)
            {
                useCase.Tick(DeltaTime);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                useCase.Tick(DeltaTime);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "PlaybackUseCase steady-state Tick path must not allocate GC.");
        }

        [Test]
        public void PlaybackUseCase_LargeValueProviderSteadyState_AfterWarmup_AllocatesZeroGC()
        {
            var triggerPort = new NullTriggerInjectionPort();
            var expressionPort = new NullExpressionInjectionPort();
            var analogPort = new NullAnalogInjectionPort();
            var valueProviderPort = new NullValueProviderInjectionPort();
            var useCase = new PlaybackUseCase(triggerPort, expressionPort, analogPort, valueProviderPort);
            RecTimeline timeline = CreateLargeValueProviderPlaybackTimeline(WarmupFrames + MeasurementFrames + 1);

            useCase.Load(timeline, CreateProfile());
            Assert.That(useCase.StartPlayback(), Is.True);

            for (int i = 0; i < WarmupFrames; i++)
            {
                useCase.Tick(DeltaTime);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                useCase.Tick(DeltaTime);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "PlaybackUseCase large value-provider steady-state Tick path must not allocate GC.");
        }

        [Test]
        public void PlaybackUseCase_WeightSteadyState_AfterWarmup_AllocatesZeroGC()
        {
            var weightPort = new NullWeightInjectionPort();
            var triggerPort = new NullTriggerInjectionPort();
            var expressionPort = new NullExpressionInjectionPort();
            var analogPort = new NullAnalogInjectionPort();
            var valueProviderPort = new NullValueProviderInjectionPort();
            var useCase = new PlaybackUseCase(
                weightPort,
                triggerPort,
                expressionPort,
                analogPort,
                valueProviderPort);
            RecTimeline timeline = CreateWeightPlaybackTimeline(WarmupFrames + MeasurementFrames + 1);

            useCase.Load(timeline, CreateProfile());
            Assert.That(useCase.StartPlayback(), Is.True);

            for (int i = 0; i < WarmupFrames; i++)
            {
                useCase.Tick(DeltaTime);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                useCase.Tick(DeltaTime);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "PlaybackUseCase weight steady-state Tick path must not allocate GC.");
        }

        [Test]
        public void FacialInputObservationBus_WithoutObservers_PublishHotPath_AllocatesZeroGC()
        {
            var bus = new FacialInputObservationBus();
            float[] axes = { 0.1f, -0.2f };

            for (int i = 0; i < WarmupFrames; i++)
            {
                bus.OnTriggerOn("input:trigger", "smile");
                bus.OnTriggerOff("input:trigger", "smile");
                bus.PublishAnalogSample("input:gaze", axes);
            }

            ForceFullCollection();
            using var recorder = StartGcRecorder();

            for (int i = 0; i < MeasurementFrames; i++)
            {
                bus.OnTriggerOn("input:trigger", "smile");
                bus.OnTriggerOff("input:trigger", "smile");
                bus.PublishAnalogSample("input:gaze", axes);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "FacialInputObservationBus publish hot path must stay zero-alloc when no observers are attached.");
        }

        private static void PublishRecordingFrame(
            RecordingUseCase useCase,
            ManualRecClock clock,
            float[] axes,
            int frameIndex)
        {
            clock.SetElapsedSeconds(frameIndex * DeltaTime);
            useCase.OnTriggerOn("input:trigger", "smile");
            useCase.OnTriggerOff("input:trigger", "smile");
            useCase.OnAnalogSample("input:gaze", axes);
        }

        private static void PublishWeightFrame(
            RecordingUseCase useCase,
            ManualRecClock clock,
            string[] layers,
            string[] slots,
            int frameIndex)
        {
            clock.SetElapsedSeconds(frameIndex * DeltaTime);
            float weight = (frameIndex % 100) / 100f;
            for (int l = 0; l < layers.Length; l++)
            {
                useCase.OnLayerWeightSample(layers[l], weight);
                for (int s = 0; s < slots.Length; s++)
                {
                    useCase.OnInputSourceWeightSample(layers[l], slots[s], 1f - weight);
                }
            }
        }

        private static void PublishLargeValueProviderFrame(
            FacialInputObservationBus observationBus,
            ManualRecClock clock,
            float[] values,
            BitArray mask,
            int frameIndex)
        {
            clock.SetElapsedSeconds(frameIndex * DeltaTime);
            var sample = new ValueProviderSample(
                true,
                false,
                true,
                true,
                values,
                mask);
            observationBus.PublishValueProviderSample("input:vector", in sample);
        }

        private static RecTimeline CreatePlaybackTimeline(int frameCount)
        {
            var events = new RecEvent[frameCount];
            var analogAxes = new IReadOnlyList<float>[frameCount];
            for (int i = 0; i < frameCount; i++)
            {
                events[i] = RecEvent.CreateAnalogSample((i + 1) * DeltaTime, 0, 2);
                analogAxes[i] = new[] { 0.25f, -0.5f };
            }

            return new RecTimeline(
                RecBaselineState.Empty,
                events,
                new[] { "input:gaze" },
                Array.Empty<string>(),
                (frameCount + 1) * DeltaTime,
                analogAxes);
        }

        private static RecTimeline CreateLargeValueProviderPlaybackTimeline(int frameCount)
        {
            var events = new RecEvent[frameCount];
            var valuesByEvent = new IReadOnlyList<float>[frameCount];
            var masksByEvent = new IReadOnlyList<byte>[frameCount];
            byte[] mask = CreateMaskBytes();
            for (int i = 0; i < frameCount; i++)
            {
                events[i] = RecEvent.CreateValueProviderSample(
                    (i + 1) * DeltaTime,
                    0,
                    RecValueProviderFlags.IsValid
                        | RecValueProviderFlags.HasMask
                        | RecValueProviderFlags.HasValues,
                    LargeBlendShapeCount,
                    (ushort)mask.Length);
                valuesByEvent[i] = CreateLargeValues();
                masksByEvent[i] = mask;
            }

            return new RecTimeline(
                new RecBaselineState(
                    null,
                    null,
                    new[]
                    {
                        new RecBaselineState.ValueProviderEntry(
                            "input:vector",
                            true,
                            mask,
                            CreateLargeValues()),
                    },
                    null),
                events,
                new[] { "input:vector" },
                Array.Empty<string>(),
                (frameCount + 1) * DeltaTime,
                valuesByEvent,
                masksByEvent);
        }

        private static RecTimeline CreateWeightPlaybackTimeline(int frameCount)
        {
            var events = new RecEvent[frameCount * 2];
            var weightsByEvent = new IReadOnlyList<float>[events.Length];
            for (int i = 0; i < frameCount; i++)
            {
                double timestamp = (i + 1) * DeltaTime;
                float weight = (i % 100) / 100f;
                int eventIndex = i * 2;
                events[eventIndex] = RecEvent.CreateLayerWeightSample(timestamp, 0);
                events[eventIndex + 1] = RecEvent.CreateInputSourceWeightSample(timestamp, 0, 0);
                weightsByEvent[eventIndex] = new[] { weight };
                weightsByEvent[eventIndex + 1] = new[] { 1f - weight };
            }

            return new RecTimeline(
                new RecBaselineState(
                    null,
                    null,
                    null,
                    null,
                    new[] { new Hidano.FacialControl.Domain.Models.LayerWeightEntry("layer", 1f) },
                    new[] { new Hidano.FacialControl.Domain.Models.InputSourceWeightEntry("layer", "input:source", 1f) }),
                events,
                new[] { "input:source" },
                Array.Empty<string>(),
                new[] { "layer" },
                (frameCount + 1) * DeltaTime,
                weightsByEvent);
        }

        private static float[] CreateLargeValues()
        {
            var values = new float[LargeBlendShapeCount];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = i / (float)LargeBlendShapeCount;
            }

            return values;
        }

        private static BitArray CreateFullMask()
        {
            return new BitArray(LargeBlendShapeCount, true);
        }

        private static byte[] CreateMaskBytes()
        {
            var mask = new byte[(LargeBlendShapeCount + 7) / 8];
            for (int i = 0; i < LargeBlendShapeCount; i++)
            {
                mask[i >> 3] |= (byte)(1 << (i & 7));
            }

            return mask;
        }

        private static Hidano.FacialControl.Domain.Models.FacialProfile CreateProfile()
        {
            return new Hidano.FacialControl.Domain.Models.FacialProfile(
                "1.0.0",
                Array.Empty<Hidano.FacialControl.Domain.Models.LayerDefinition>(),
                Array.Empty<Hidano.FacialControl.Domain.Models.Expression>());
        }

        private static ProfilerRecorder StartGcRecorder()
        {
            return ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);
        }

        private static void ForceFullCollection()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private sealed class ManualRecClock : IRecClock
        {
            public double ElapsedSeconds { get; private set; }

            public void Reset()
            {
                ElapsedSeconds = 0d;
            }

            public void SetElapsedSeconds(double elapsedSeconds)
            {
                ElapsedSeconds = elapsedSeconds;
            }
        }

        private sealed class NullRecEventSink : IRecEventSink
        {
            public void Open(RecBaselineState baseline)
            {
            }

            public void AppendEvent(in RecEvent evt, ReadOnlySpan<float> payload, ReadOnlySpan<byte> maskBytes = default, string idValue = null)
            {
            }

            public void Complete(double durationSeconds, int eventCount)
            {
            }
        }

        private sealed class NullTriggerInjectionPort : ITriggerInjectionPort
        {
            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                return true;
            }

            public void InjectTriggerOn(string sourceId, string expressionId)
            {
            }

            public void InjectTriggerOff(string sourceId, string expressionId)
            {
            }

            public void EndInjection()
            {
            }
        }

        private sealed class NullExpressionInjectionPort : IExpressionInjectionPort
        {
            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                return true;
            }

            public void InjectActivate(string expressionId)
            {
            }

            public void InjectDeactivate(string expressionId)
            {
            }

            public void EndInjection()
            {
            }
        }

        private sealed class NullAnalogInjectionPort : IAnalogInjectionPort
        {
            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                return true;
            }

            public void InjectAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
            }

            public void EndInjection()
            {
            }
        }

        private sealed class NullWeightInjectionPort : IWeightInjectionPort
        {
            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                return true;
            }

            public void InjectLayerWeight(string layerName, float weight)
            {
            }

            public void InjectInputSourceWeight(string layerName, string slotId, float weight)
            {
            }

            public void EndInjection()
            {
            }
        }

        private sealed class NullValueProviderInjectionPort : IValueProviderInjectionPort
        {
            public bool CanBeginInjection(out string reason)
            {
                reason = string.Empty;
                return true;
            }

            public bool TryBeginInjection(RecBaselineState baseline)
            {
                return true;
            }

            public void InjectValueProviderState(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
            {
            }

            public void EndInjection()
            {
            }
        }
    }
}
