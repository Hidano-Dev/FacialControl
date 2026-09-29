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

            HandleFloat(OscAddressFormatter.VRChatParameterPrefix + BlendShapeName, 0.72f);
            _binding.OnFixedTick(0.02f);
            AssertBlendShape(0.72f);

            time.UnscaledTimeSeconds = 0.75d;
            _binding.OnFixedTick(0.02f);
            AssertBlendShape(0f);
            Assert.That(_binding.InputSource.IsStale, Is.True);

            HandleFloat(OscAddressFormatter.VRChatParameterPrefix + BlendShapeName, 0.31f);
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

            HandleFloat(OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId + "X", 0.44f);
            HandleFloat(OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId + "Y", -0.27f);
            _binding.OnFixedTick(0.02f);
            AssertGaze(registry, 0.44f, -0.27f);

            time.UnscaledTimeSeconds = 0.75d;
            _binding.OnFixedTick(0.02f);
            AssertGaze(registry, 0f, 0f);

            HandleFloat(OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId + "X", -0.12f);
            HandleFloat(OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId + "Y", 0.63f);
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
            binding.Configure(
                "127.0.0.1",
                binding.Port,
                new[]
                {
                    new OscMapping(
                        OscAddressFormatter.VRChatParameterPrefix + BlendShapeName,
                        BlendShapeName,
                        string.Empty)
                });
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
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = GazeExpressionId,
                        addressPattern = OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId
                    }
                }
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

        private void HandleFloat(string address, float value)
        {
            _binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message(address, value));
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
