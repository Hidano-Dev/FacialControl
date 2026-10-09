using System;
using System.Collections.Generic;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Profiling;

using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Osc.Tests.PlayMode.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    [TestFixture]
    [MediumTest]
    public sealed class OscReceiverGCAllocationTests : SizedTestFixture
    {
        private const int FrameCount = 100;
        private const int PortBase = 19700;
        private const string Slug = "osc-receiver-gc";
        private static readonly string[] BlendShapeNames = { "smile", "frown" };
        private static readonly SenderIdentity Sender =
            new SenderIdentity(Guid.Parse("55555555-5555-5555-5555-555555555555"), 1_000L);

        private static int s_portCounter;

        private GameObject _host;
        private OscReceiverAdapterBinding _binding;

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
        public void OnFixedTick_IndividualBlendShapeMessages100Frames_RecordsBaseline()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.IndividualMessage, includeGaze: false);

            uOSC.Message[] messages = FrameMessages(100UL, 0.25f, 0.75f);

            WarmUp(messages, timeProvider);

            BaselineResult baseline = MeasureFrames(() =>
            {
                HandleMessages(messages);
                _binding.OnFixedTick(1f / 60f);
            });

            Assert.That(registry.TryResolve(Slug, out IInputSource source), Is.True);
            var output = new float[2];
            Assert.That(source.TryWriteValues(output), Is.True);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(0.75f).Within(1e-6f));
            LogBaseline(nameof(OscReceiverGCAllocationTests), "individualBlendShapeMessages", baseline);
        }

        [Test]
        public void OnFixedTick_AtomicBundleGazeMessages100Frames_RecordsBaseline()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.AtomicSwap, includeGaze: true);

            // slot: smile, frown, eye.X, eye.Y
            uOSC.Message[] messages = FrameMessages(100UL, 0.25f, 0.75f, -0.4f, 0.6f);

            WarmUp(messages, timeProvider);

            BaselineResult baseline = MeasureFrames(() =>
            {
                HandleMessages(messages);
                timeProvider.UnscaledTimeSeconds += 0.001d;
                _binding.OnFixedTick(1f / 60f);
            });

            GazeVector2InputSource gaze = ResolveGaze(registry, Slug + ":eye");
            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-0.4f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(0.6f).Within(1e-6f));
            LogBaseline(nameof(OscReceiverGCAllocationTests), "atomicBundleGazeMessages", baseline);
        }

        [Test]
        public void GazeVector2InputSource_Read_ZeroAlloc()
        {
            var registry = new InputSourceRegistry();
            var timeProvider = new ManualTimeProvider();
            StartReceiver(registry, timeProvider, BundleInterpretationMode.IndividualMessage, includeGaze: true);
            GazeVector2InputSource source = ResolveGaze(registry, Slug + ":eye");
            source.Publish(0.25f, -0.5f);

            for (int i = 0; i < 16; i++)
            {
                Assert.That(source.TryReadVector2(out _, out _), Is.True);
            }

            StabilizeManagedHeap();
            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int read = 0; read < FrameCount; read++)
            {
                Assert.That(source.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.5f).Within(1e-6f));
            }

            Assert.That(recorder.LastValue, Is.EqualTo(0L),
                "gaze source read reported GC.Alloc: " + recorder.LastValue + " bytes.");
        }

        private void StartReceiver(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            BundleInterpretationMode bundleMode,
            bool includeGaze)
        {
            _host = new GameObject("OscReceiverGCAllocationTests");
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = bundleMode,
                BundleAccumulationTimeoutMs = 0f,
            };

            _binding.OnStart(CreateContext(registry, timeProvider, BlendShapeNames));
            OscIndexedFrameMessages.ApplyLayout(
                _binding,
                Sender,
                BlendShapeNames,
                includeGaze ? new[] { new OscFrameLayoutGazeChannel("eye") } : null,
                timestamp: 50UL);

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
            Assert.That(_binding.ActiveLayoutVersion, Is.EqualTo(OscIndexedFrameMessages.LayoutVersion));
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider,
            string[] blendShapeNames = null)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames ?? Array.Empty<string>(),
                registry,
                new FacialOutputBus(),
                timeProvider,
                _host,
                lipSyncProvider: null);
        }

        private void WarmUp(uOSC.Message[] messages, ManualTimeProvider timeProvider)
        {
            for (int i = 0; i < 4; i++)
            {
                HandleMessages(messages);
                timeProvider.UnscaledTimeSeconds += 0.001d;
                _binding.OnFixedTick(1f / 60f);
            }
        }

        private void HandleMessages(uOSC.Message[] messages)
        {
            for (int i = 0; i < messages.Length; i++)
            {
                _binding.HelperHost.Receiver.HandleOscMessage(messages[i]);
            }
        }

        /// <summary>sender_id と値フレーム（同じ bundle）。値は slot 0 から並べる。</summary>
        private static uOSC.Message[] FrameMessages(ulong timestamp, params float[] slots)
        {
            return new[]
            {
                OscIndexedFrameMessages.SenderId(Sender, timestamp),
                OscIndexedFrameMessages.Values(OscIndexedFrameMessages.LayoutVersion, 0, timestamp, slots),
            };
        }

        private static GazeVector2InputSource ResolveGaze(InputSourceRegistry registry, string id)
        {
            Assert.That(registry.TryResolve(id, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<GazeVector2InputSource>());
            return (GazeVector2InputSource)source;
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
