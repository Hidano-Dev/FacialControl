using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
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
    public sealed class OscMultiEndpointTests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19420;
        private const string Smile = "smile";
        private const string Blink = "blink";
        private const string GazeExpressionId = "eye-look";
        private const float SmileValue = 0.42f;
        private const float BlinkValue = 0.84f;
        private const float GazeX = 0.35f;
        private const float GazeY = -0.55f;
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
        public IEnumerator OneSender_TwoReceivers_ReceivesBlendShapesAndGazeOnBothEndpoints()
        {
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            var outputBus = new FacialOutputBus();
            var firstRegistry = new InputSourceRegistry();
            var secondRegistry = new InputSourceRegistry();

            OscReceiverAdapterBinding firstReceiver = CreateReceiver("osc-receiver-a", firstPort);
            OscReceiverAdapterBinding secondReceiver = CreateReceiver("osc-receiver-b", secondPort);
            OscSenderAdapterBinding sender = CreateSender(firstPort, secondPort);

            StartBinding(
                firstReceiver,
                CreateContext(
                    firstRegistry,
                    new FacialOutputBus(),
                    CreateGameObject("OscMultiEndpointTests_ReceiverA"),
                    new UnityTimeProvider(),
                    new[] { Smile, Blink }));
            StartBinding(
                secondReceiver,
                CreateContext(
                    secondRegistry,
                    new FacialOutputBus(),
                    CreateGameObject("OscMultiEndpointTests_ReceiverB"),
                    new UnityTimeProvider(),
                    new[] { Smile, Blink }));

            yield return new WaitForSecondsRealtime(0.2f);

            StartBinding(
                sender,
                CreateContext(
                    new InputSourceRegistry(),
                    outputBus,
                    CreateGameObject("OscMultiEndpointTests_Sender"),
                    new UnityTimeProvider(),
                    new[] { Smile, Blink }));

            yield return new WaitForSecondsRealtime(0.2f);

            float[] postBlendValues = { SmileValue, BlinkValue };
            var gazeSnapshots = new[] { new GazeSnapshot(GazeExpressionId, GazeX, GazeY) };
            bool bothEndpointsReached = false;

            // 受信側は最初の値フレームで対応表を要求し、適用後の値フレームから書き込む。
            for (int attempt = 0; attempt < 60 && !bothEndpointsReached; attempt++)
            {
                outputBus.Publish(postBlendValues, gazeSnapshots);
                sender.OnLateTick(0.016f);

                yield return new WaitForSecondsRealtime(0.05f);

                firstReceiver.OnFixedTick(0.02f);
                secondReceiver.OnFixedTick(0.02f);

                bool firstReached = TryReadReceiver(firstReceiver, firstRegistry, "osc-receiver-a:" + GazeExpressionId);
                bool secondReached = TryReadReceiver(secondReceiver, secondRegistry, "osc-receiver-b:" + GazeExpressionId);
                bothEndpointsReached = firstReached && secondReached;
            }

            Assert.That(bothEndpointsReached, Is.True);
        }

        private static OscReceiverAdapterBinding CreateReceiver(string slug, int port)
        {
            return new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                BundleAccumulationTimeoutMs = 5f
            };
        }

        private static OscSenderAdapterBinding CreateSender(int firstPort, int secondPort)
        {
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-multi-endpoint-sender",
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f
            };
            binding.ConfigureEndpoints(
                new[]
                {
                    new OscSenderEndpointConfig(Endpoint, firstPort, true),
                    new OscSenderEndpointConfig(Endpoint, secondPort, true)
                },
                new[] { Smile, Blink });
            binding.ConfigureGazeChannels(new[] { GazeExpressionId });
            return binding;
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            FacialOutputBus outputBus,
            GameObject host,
            ITimeProvider timeProvider,
            IReadOnlyList<string> blendShapeNames)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames,
                registry,
                outputBus,
                timeProvider,
                host,
                lipSyncProvider: null);
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

        private static bool TryReadReceiver(
            OscReceiverAdapterBinding receiver,
            InputSourceRegistry registry,
            string gazeSourceId)
        {
            var blendShapeValues = new float[2];
            if (receiver.InputSource == null ||
                !receiver.InputSource.TryWriteValues(blendShapeValues))
            {
                return false;
            }

            if (!Approximately(blendShapeValues[0], SmileValue) ||
                !Approximately(blendShapeValues[1], BlinkValue))
            {
                return false;
            }

            if (!registry.TryResolve(gazeSourceId, out IInputSource source) ||
                source is not IAnalogInputSource gazeSource ||
                !gazeSource.TryReadVector2(out float x, out float y))
            {
                return false;
            }

            return Approximately(x, GazeX) && Approximately(y, GazeY);
        }

        private static bool Approximately(float actual, float expected)
        {
            return Math.Abs(actual - expected) <= Tolerance;
        }

        private static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }
    }
}
