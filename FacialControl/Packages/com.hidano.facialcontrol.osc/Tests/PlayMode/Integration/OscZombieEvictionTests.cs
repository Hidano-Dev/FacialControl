using System;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Osc.Tests.PlayMode.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    [MediumTest]
    public sealed class OscZombieEvictionTests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int PortBase = 19620;
        private const string Slug = "osc-zombie-eviction";
        private const string BlendShapeName = "smile";

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
        public void OnFixedTick_MultipleSenderIdentities_IgnoresOlderStartupValue()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0d };
            _host = new GameObject("OscZombieEvictionTests");
            _binding = CreateReceiver();
            _binding.OnStart(CreateContext(registry, time));

            var oldSender = new SenderIdentity(
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                1_000L);
            var newSender = new SenderIdentity(
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                2_000L);

            LogAssert.Expect(LogType.Log, SenderSwitchLog(oldSender, newSender));

            OscReceiver receiver = _binding.HelperHost.Receiver;
            OscIndexedFrameMessages.SendFrame(receiver, oldSender, 100UL, 0.2f);
            OscIndexedFrameMessages.ApplyLayout(_binding, newSender, new[] { BlendShapeName }, timestamp: 150UL);
            OscIndexedFrameMessages.SendFrame(receiver, newSender, 200UL, 0.8f);
            OscIndexedFrameMessages.SendFrame(receiver, oldSender, 300UL, 0.1f);

            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.CurrentSenderId.HasValue, Is.True);
            Assert.That(_binding.CurrentSenderId.Value, Is.EqualTo(newSender));
            Assert.That(_binding.ZombiePolicy.IsAccepted(oldSender), Is.False);
            AssertBlendShape(0.8f);
        }

        private static OscReceiverAdapterBinding CreateReceiver()
        {
            var binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            return binding;
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

        private void AssertBlendShape(float expected)
        {
            var values = new float[1];
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_binding.InputSource.TryWriteValues(values), Is.True);
            Assert.That(values[0], Is.EqualTo(expected).Within(1e-6f));
        }

        private static Regex SenderSwitchLog(
            SenderIdentity previous,
            SenderIdentity current)
        {
            string pattern = "ZombieEvictionPolicy adopted sender changed:.*"
                + "previousUuid=" + Regex.Escape(previous.SenderId.ToString("D")) + ".*"
                + "currentUuid=" + Regex.Escape(current.SenderId.ToString("D"));
            return new Regex(pattern);
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }
    }
}
