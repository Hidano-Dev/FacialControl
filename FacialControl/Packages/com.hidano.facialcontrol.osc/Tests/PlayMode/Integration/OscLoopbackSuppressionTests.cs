using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    [MediumTest]
    public sealed class OscLoopbackSuppressionTests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int PortBase = 19520;
        private const string BlendShapeName = "smile";
        private const string SenderSlug = "osc-loopback-sender";
        private const string ReceiverSlug = "osc-loopback-receiver";
        private const float SentValue = 0.66f;
        private const float Tolerance = 0.06f;

        private static int s_portCounter;

        private readonly List<AdapterBindingBase> _startedBindings = new List<AdapterBindingBase>();
        private readonly List<GameObject> _gameObjects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _startedBindings.Count - 1; i >= 0; i--)
            {
                _startedBindings[i]?.Dispose();
            }
            _startedBindings.Clear();

            for (int i = _gameObjects.Count - 1; i >= 0; i--)
            {
                GameObject gameObject = _gameObjects[i];
                if (gameObject == null)
                {
                    continue;
                }

                gameObject.SetActive(false);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
            _gameObjects.Clear();
        }

        [UnityTest]
        public IEnumerator OnLateTick_DefaultLoopbackSuppression_DoesNotSendToSameScopeReceiver()
        {
            int port = AllocatePort();
            var registry = new InputSourceRegistry();
            var outputBus = new FacialOutputBus();
            GameObject host = CreateGameObject("OscLoopbackSuppressionTests_DefaultOn");
            OscReceiverAdapterBinding receiver = CreateReceiver(port);
            OscSenderAdapterBinding sender = CreateSender(port, suppressLoopback: true);
            AdapterBindingBase[] sameScopeBindings = { receiver, sender };

            StartBinding(receiver, CreateContext(registry, outputBus, host, sameScopeBindings));

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"\[OscSenderAdapterBinding\] Endpoint '127\.0\.0\.1:" + port + "'.*suppressed"));
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"\[OscSenderAdapterBinding\] All endpoints were suppressed"));

            StartBinding(sender, CreateContext(registry, outputBus, host, sameScopeBindings));

            Assert.That(sender.IsStarted, Is.True);
            Assert.That(sender.HelperSenderCount, Is.EqualTo(0));
            Assert.That(sender.LoopbackPolicy, Is.Not.Null);

            outputBus.Publish(new[] { SentValue }, Array.Empty<GazeSnapshot>());
            sender.OnLateTick(0.016f);

            yield return new WaitForSecondsRealtime(0.2f);

            receiver.OnFixedTick(0.02f);
            AssertBlendShape(receiver, expected: 0f, tolerance: 1e-6f);
        }

        [UnityTest]
        public IEnumerator OnLateTick_LoopbackSuppressionDisabled_SendsToSameScopeReceiver()
        {
            int port = AllocatePort();
            var registry = new InputSourceRegistry();
            var outputBus = new FacialOutputBus();
            GameObject host = CreateGameObject("OscLoopbackSuppressionTests_Disabled");
            OscReceiverAdapterBinding receiver = CreateReceiver(port);
            OscSenderAdapterBinding sender = CreateSender(port, suppressLoopback: false);
            AdapterBindingBase[] sameScopeBindings = { receiver, sender };

            StartBinding(receiver, CreateContext(registry, outputBus, host, sameScopeBindings));
            yield return new WaitForSecondsRealtime(0.1f);

            StartBinding(sender, CreateContext(registry, outputBus, host, sameScopeBindings));

            Assert.That(sender.IsStarted, Is.True);
            Assert.That(sender.HelperSenderCount, Is.EqualTo(1));
            Assert.That(sender.LoopbackPolicy, Is.Null);

            bool received = false;
            for (int attempt = 0; attempt < 20 && !received; attempt++)
            {
                outputBus.Publish(new[] { SentValue }, Array.Empty<GazeSnapshot>());
                sender.OnLateTick(0.016f);

                yield return new WaitForSecondsRealtime(0.05f);

                receiver.OnFixedTick(0.02f);
                received = TryReadBlendShape(receiver, out float actual)
                    && Math.Abs(actual - SentValue) <= Tolerance;
            }

            Assert.That(received, Is.True);
        }

        private static OscReceiverAdapterBinding CreateReceiver(int port)
        {
            var receiver = new OscReceiverAdapterBinding
            {
                Slug = ReceiverSlug,
                BundleMode = BundleInterpretationMode.IndividualMessage
            };
            receiver.Configure(
                Endpoint,
                port,
                new[]
                {
                    new OscMapping(
                        OscAddressFormatter.VRChatParameterPrefix + BlendShapeName,
                        BlendShapeName,
                        string.Empty)
                });
            return receiver;
        }

        private static OscSenderAdapterBinding CreateSender(int port, bool suppressLoopback)
        {
            var sender = new OscSenderAdapterBinding
            {
                Slug = SenderSlug,
                SuppressLoopback = suppressLoopback,
                HeartbeatIntervalSeconds = 60f
            };
            sender.Configure(Endpoint, port, new[] { BlendShapeName });
            return sender;
        }

        private static AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            FacialOutputBus outputBus,
            GameObject host,
            IReadOnlyList<AdapterBindingBase> adapterBindings)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                new[] { BlendShapeName },
                registry,
                outputBus,
                new UnityTimeProvider(),
                host,
                lipSyncProvider: null,
                adapterBindings: adapterBindings);
        }

        private void StartBinding(AdapterBindingBase binding, AdapterBuildContext context)
        {
            binding.OnStart(in context);
            _startedBindings.Add(binding);
        }

        private GameObject CreateGameObject(string name)
        {
            var gameObject = new GameObject(name);
            _gameObjects.Add(gameObject);
            return gameObject;
        }

        private static void AssertBlendShape(
            OscReceiverAdapterBinding receiver,
            float expected,
            float tolerance)
        {
            Assert.That(TryReadBlendShape(receiver, out float actual), Is.True);
            Assert.That(actual, Is.EqualTo(expected).Within(tolerance));
        }

        private static bool TryReadBlendShape(OscReceiverAdapterBinding receiver, out float value)
        {
            value = default;
            if (receiver.InputSource == null)
            {
                return false;
            }

            var values = new float[1];
            if (!receiver.InputSource.TryWriteValues(values))
            {
                return false;
            }

            value = values[0];
            return true;
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }
    }
}
