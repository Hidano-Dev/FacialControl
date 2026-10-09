using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Osc.Tests.PlayMode.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    [MediumTest]
    public sealed class OscFailSafeRevertTests : SizedTestFixture
    {
        private const int PortBase = 19580;
        private const string BlendShapeName = "smile";
        private const string GazeExpressionId = "eye-look";
        private const float StalenessSeconds = 0.5f;

        private static readonly SenderIdentity Sender =
            new SenderIdentity(Guid.Parse("33333333-3333-3333-3333-333333333333"), 1_000L);

        private static int s_portCounter;

        private GameObject _host;
        private OscReceiverAdapterBinding _binding;

        [TearDown]
        public void TearDown()
        {
            _binding?.Dispose();
            _binding = null;

            if (_host != null)
            {
                _host.SetActive(false);
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void OnFixedTick_BlendShapeStaleness_RevertsToBaseAndRestoresOnNewPacket()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0d };
            _host = new GameObject("OscFailSafeRevertTests_BlendShape");
            _binding = CreateBlendShapeReceiver();
            _binding.OnStart(CreateContext(registry, time));
            OscIndexedFrameMessages.ApplyLayout(_binding, Sender, new[] { BlendShapeName });

            SendFrame(2000UL, 0.72f);
            _binding.OnFixedTick(0.02f);
            AssertBlendShape(0.72f);

            time.UnscaledTimeSeconds = 0.75d;
            _binding.OnFixedTick(0.02f);
            AssertBlendShape(0f);
            Assert.That(_binding.InputSource.IsStale, Is.True);

            SendFrame(3000UL, 0.31f);
            _binding.OnFixedTick(0.02f);
            AssertBlendShape(0.31f);
            Assert.That(_binding.InputSource.IsStale, Is.False);
        }

        [Test]
        public void OnFixedTick_GazeStaleness_RevertsToCenterAndRestoresOnNewPacket()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0d };
            _host = new GameObject("OscFailSafeRevertTests_Gaze");
            _binding = CreateGazeReceiver();
            _binding.OnStart(CreateContext(registry, time));
            OscIndexedFrameMessages.ApplyLayout(
                _binding,
                Sender,
                Array.Empty<string>(),
                new[] { new OscFrameLayoutGazeChannel(GazeExpressionId) });

            SendFrame(2000UL, 0.44f, -0.27f);
            _binding.OnFixedTick(0.02f);
            AssertGaze(registry, 0.44f, -0.27f);

            time.UnscaledTimeSeconds = 0.75d;
            _binding.OnFixedTick(0.02f);
            AssertGaze(registry, 0f, 0f);

            SendFrame(3000UL, -0.12f, 0.63f);
            _binding.OnFixedTick(0.02f);
            AssertGaze(registry, -0.12f, 0.63f);
        }

        private static OscReceiverAdapterBinding CreateBlendShapeReceiver()
        {
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-failsafe-blend",
                Port = AllocatePort(),
                StalenessSeconds = StalenessSeconds,
                FailSafeMode = FailSafeMode.RevertToBase,
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            return binding;
        }

        private static OscReceiverAdapterBinding CreateGazeReceiver()
        {
            return new OscReceiverAdapterBinding
            {
                Slug = "osc-failsafe-gaze",
                Port = AllocatePort(),
                StalenessSeconds = StalenessSeconds,
                FailSafeMode = FailSafeMode.RevertToBase,
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                new[] { BlendShapeName },
                registry,
                new FacialOutputBus(),
                timeProvider,
                _host,
                lipSyncProvider: null);
        }

        private void SendFrame(ulong timestamp, params float[] slots)
        {
            OscIndexedFrameMessages.SendFrame(_binding.HelperHost.Receiver, Sender, timestamp, slots);
        }

        private void AssertBlendShape(float expected)
        {
            var values = new float[1];
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_binding.InputSource.TryWriteValues(values), Is.True);
            Assert.That(values[0], Is.EqualTo(expected).Within(1e-6f));
        }

        private static void AssertGaze(
            InputSourceRegistry registry,
            float expectedX,
            float expectedY)
        {
            Assert.That(
                registry.TryResolve("osc-failsafe-gaze:" + GazeExpressionId, out IInputSource source),
                Is.True);
            Assert.That(source, Is.InstanceOf<IAnalogInputSource>());

            var gaze = (IAnalogInputSource)source;
            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(expectedX).Within(1e-6f));
            Assert.That(y, Is.EqualTo(expectedY).Within(1e-6f));
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }
    }
}
