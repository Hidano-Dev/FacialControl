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
    public sealed class OscBundleAtomicityTests : SizedTestFixture
    {
        private const int PortBase = 19480;
        private const string Slug = "osc-bundle-atomicity";
        private const string Smile = "smile";
        private const string Blink = "blink";
        private const string GazeExpressionId = "eye-look";

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
        public void OnFixedTick_SameTimestampBundle_ReflectsAllMessagesTogetherAfterTimeout()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0d };
            _host = new GameObject("OscBundleAtomicityTests");
            _binding = CreateReceiver();
            _binding.OnStart(CreateContext(registry, time));

            const ulong timestamp = 100UL;
            SenderIdentity identity = new SenderIdentity(Guid.NewGuid(), 1_000L);

            _binding.HelperHost.Receiver.HandleOscMessage(SenderMessage(identity, timestamp));
            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage(OscAddressFormatter.VRChatParameterPrefix + Smile, 0.25f, timestamp));
            _binding.OnFixedTick(0.02f);

            AssertBlendShapes(new[] { 0f, 0f });
            AssertGazeUnavailable(registry);

            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage(OscAddressFormatter.VRChatParameterPrefix + Blink, 0.75f, timestamp));
            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage(OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId + "X", 0.4f, timestamp));
            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage(OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId + "Y", -0.6f, timestamp));
            _binding.OnFixedTick(0.02f);

            AssertBlendShapes(new[] { 0f, 0f });
            AssertGazeUnavailable(registry);

            time.UnscaledTimeSeconds = 0.006d;
            _binding.OnFixedTick(0.02f);

            AssertBlendShapes(new[] { 0.25f, 0.75f });
            AssertGaze(registry, 0.4f, -0.6f);
        }

        private static OscReceiverAdapterBinding CreateReceiver()
        {
            return new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                BundleAccumulationTimeoutMs = 5f,
                Mappings = new List<OscMappingEntry>
                {
                    CreateBlendShapeEntry(Smile),
                    CreateBlendShapeEntry(Blink),
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = GazeExpressionId,
                        addressPattern = OscAddressFormatter.VRChatParameterPrefix + GazeExpressionId
                    }
                }
            };
        }

        private static OscMappingEntry CreateBlendShapeEntry(string name)
        {
            return new OscMappingEntry
            {
                mode = OscMappingMode.Normal_BlendShape,
                expressionId = name,
                addressPattern = OscAddressFormatter.VRChatParameterPrefix + name
            };
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            ManualTimeProvider timeProvider)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                Array.Empty<string>(),
                registry,
                new FacialOutputBus(),
                timeProvider,
                _host,
                lipSyncProvider: null);
        }

        private void AssertBlendShapes(float[] expected)
        {
            var actual = new float[expected.Length];
            Assert.That(_binding.InputSource.TryWriteValues(actual), Is.True);
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-6f));
            }
        }

        private static void AssertGazeUnavailable(InputSourceRegistry registry)
        {
            Assert.That(registry.TryResolve(Slug + ":" + GazeExpressionId, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<IAnalogInputSource>());
            var gaze = (IAnalogInputSource)source;
            Assert.That(gaze.TryReadVector2(out _, out _), Is.False);
        }

        private static void AssertGaze(InputSourceRegistry registry, float expectedX, float expectedY)
        {
            Assert.That(registry.TryResolve(Slug + ":" + GazeExpressionId, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<IAnalogInputSource>());
            var gaze = (IAnalogInputSource)source;
            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(expectedX).Within(1e-6f));
            Assert.That(y, Is.EqualTo(expectedY).Within(1e-6f));
        }

        private static uOSC.Message SenderMessage(SenderIdentity identity, ulong timestamp)
        {
            var message = new uOSC.Message(
                OscReceiverAdapterBinding.SenderIdentityAddress,
                identity.SenderId.ToByteArray(),
                identity.StartedAtUnixMs);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        private static uOSC.Message FloatMessage(string address, float value, ulong timestamp)
        {
            var message = new uOSC.Message(address, value);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }
    }
}
