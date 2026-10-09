using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using Unity.Profiling;
using UnityEngine.Profiling;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public sealed class OscSenderGCAllocationTests : SizedTestFixture
    {
        private const int FrameCount = 100;
        private const int PortBase = 19600;
        private const string Endpoint = "127.0.0.1";
        private const string GazeExpressionId = "eyeLook";

        private static int s_portCounter;

        private GameObject _host;
        private OscSenderAdapterBinding _binding;

        [TearDown]
        public void TearDown()
        {
            if (_binding != null)
            {
                _binding.Dispose();
                _binding = null;
            }

            if (_host != null)
            {
                _host.SetActive(false);
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void OnLateTick_BlendShapeOnly100Frames_RecordsBaseline()
        {
            string[] blendShapeNames = CreateBlendShapeNames(16);
            var bus = new FacialOutputBus();
            StartSender(bus, blendShapeNames, Array.Empty<string>(), 60f);

            float[] values = CreateValues(blendShapeNames.Length);
            WarmUp(bus, values, Array.Empty<GazeSnapshot>(), deltaTime: 1f / 60f);

            BaselineResult baseline = MeasureFrames(() =>
            {
                bus.Publish(values, Array.Empty<GazeSnapshot>());
                _binding.OnLateTick(1f / 60f);
            });

            Assert.That(_binding.IsStarted, Is.True);
            LogBaseline(nameof(OscSenderGCAllocationTests), "blendShapeOnly", baseline);
        }

        [Test]
        public void OnLateTick_BlendShapeAndGaze100Frames_RecordsBaseline()
        {
            string[] blendShapeNames = CreateBlendShapeNames(16);
            var gazeSnapshots = new[] { new GazeSnapshot(GazeExpressionId, -0.25f, 0.5f) };
            var bus = new FacialOutputBus();
            StartSender(
                bus,
                blendShapeNames,
                new[] { GazeExpressionId },
                60f);

            float[] values = CreateValues(blendShapeNames.Length);
            WarmUp(bus, values, gazeSnapshots, deltaTime: 1f / 60f);

            BaselineResult baseline = MeasureFrames(() =>
            {
                bus.Publish(values, gazeSnapshots);
                _binding.OnLateTick(1f / 60f);
            });

            Assert.That(_binding.IsStarted, Is.True);
            LogBaseline(nameof(OscSenderGCAllocationTests), "blendShapeAndGaze", baseline);
        }

        [Test]
        public void OnLateTick_LayoutRefreshEveryTick100Frames_RecordsBaseline()
        {
            string[] blendShapeNames = CreateBlendShapeNames(32);
            var bus = new FacialOutputBus();
            StartSender(
                bus,
                blendShapeNames,
                Array.Empty<string>(),
                OscSenderAdapterBinding.MinHeartbeatIntervalSeconds);

            float[] values = CreateValues(blendShapeNames.Length);
            WarmUp(bus, values, Array.Empty<GazeSnapshot>(), deltaTime: OscSenderAdapterBinding.MinHeartbeatIntervalSeconds);

            BaselineResult baseline = MeasureFrames(() =>
            {
                bus.Publish(values, Array.Empty<GazeSnapshot>());
                _binding.OnLateTick(OscSenderAdapterBinding.MinHeartbeatIntervalSeconds);
            });

            Assert.That(_binding.IsStarted, Is.True);
            LogBaseline(nameof(OscSenderGCAllocationTests), "layoutRefreshEveryTick", baseline);
        }

        [Test]
        public void Sender_LayoutRefreshEveryTickWithGaze_NoPerTickAlloc()
        {
            string[] blendShapeNames = CreateBlendShapeNames(16);
            var gazeSnapshots = new[] { new GazeSnapshot(GazeExpressionId, -0.25f, 0.5f) };
            var bus = new FacialOutputBus();
            StartSender(
                bus,
                blendShapeNames,
                new[] { GazeExpressionId },
                OscSenderAdapterBinding.MinHeartbeatIntervalSeconds);

            float[] values = CreateValues(blendShapeNames.Length);
            WarmUp(bus, values, gazeSnapshots, OscSenderAdapterBinding.MinHeartbeatIntervalSeconds);
            StabilizeManagedHeap();
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int heartbeat = 0; heartbeat < FrameCount; heartbeat++)
            {
                bus.Publish(values, gazeSnapshots);
                _binding.OnLateTick(OscSenderAdapterBinding.MinHeartbeatIntervalSeconds);
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "gaze layout refresh every tick reported GC.Alloc: " + recorder.LastValue + " bytes.");
        }

        private void StartSender(
            FacialOutputBus bus,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<string> gazeExpressionIds,
            float heartbeatIntervalSeconds)
        {
            _host = new GameObject("OscSenderGCAllocationTests");
            _binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-gc",
                HeartbeatIntervalSeconds = heartbeatIntervalSeconds,
                SuppressLoopback = false,
            };

            _binding.ConfigureEndpoints(
                new[]
                {
                    new OscSenderEndpointConfig(Endpoint, AllocatePort()),
                },
                blendShapeNames);
            _binding.ConfigureGazeChannels(gazeExpressionIds);
            _binding.OnStart(CreateContext(bus, blendShapeNames));

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperSenderCount, Is.EqualTo(1));
        }

        private AdapterBuildContext CreateContext(
            FacialOutputBus bus,
            IReadOnlyList<string> blendShapeNames)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames,
                new InputSourceRegistry(),
                bus,
                new ManualTimeProvider(),
                _host,
                lipSyncProvider: null);
        }

        private void WarmUp(
            FacialOutputBus bus,
            float[] values,
            GazeSnapshot[] gazeSnapshots,
            float deltaTime)
        {
            for (int i = 0; i < 4; i++)
            {
                bus.Publish(values, gazeSnapshots);
                _binding.OnLateTick(deltaTime);
            }
        }

        private static BaselineResult MeasureFrames(Action measureFrame)
        {
            StabilizeManagedHeap();

            long managedBefore = GC.GetTotalMemory(forceFullCollection: false);
            long profilerBefore = Profiler.GetTotalAllocatedMemoryLong();
            for (int frame = 0; frame < FrameCount; frame++)
            {
                measureFrame();
            }

            long profilerAfter = Profiler.GetTotalAllocatedMemoryLong();
            long managedAfter = GC.GetTotalMemory(forceFullCollection: false);
            return new BaselineResult(
                FrameCount,
                profilerAfter - profilerBefore,
                managedAfter - managedBefore);
        }

        private static void LogBaseline(string fixture, string scenario, BaselineResult baseline)
        {
            string message =
                $"[{fixture}] preview.2 GC baseline scenario={scenario}, frames={baseline.FrameCount}, " +
                $"profilerAllocatedDeltaBytes={baseline.ProfilerAllocatedDeltaBytes}, " +
                $"managedHeapDeltaBytes={baseline.ManagedHeapDeltaBytes}";
            TestContext.Out.WriteLine(message);
            Debug.Log(message);
        }

        private static void StabilizeManagedHeap()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        private static string[] CreateBlendShapeNames(int count)
        {
            var names = new string[count];
            for (int i = 0; i < count; i++)
            {
                names[i] = "BlendShape_" + i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture);
            }

            return names;
        }

        private static float[] CreateValues(int count)
        {
            var values = new float[count];
            for (int i = 0; i < count; i++)
            {
                values[i] = (i % 100) / 100f;
            }

            return values;
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }

        private readonly struct BaselineResult
        {
            public readonly int FrameCount;
            public readonly long ProfilerAllocatedDeltaBytes;
            public readonly long ManagedHeapDeltaBytes;

            public BaselineResult(
                int frameCount,
                long profilerAllocatedDeltaBytes,
                long managedHeapDeltaBytes)
            {
                FrameCount = frameCount;
                ProfilerAllocatedDeltaBytes = profilerAllocatedDeltaBytes;
                ManagedHeapDeltaBytes = managedHeapDeltaBytes;
            }
        }
    }
}
