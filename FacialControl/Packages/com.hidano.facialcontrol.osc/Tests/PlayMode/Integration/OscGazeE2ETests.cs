#if FACIALCONTROL_HAS_INPUTSYSTEM_MODULE && FACIALCONTROL_HAS_UNITY_INPUTSYSTEM
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Osc.Tests.PlayMode.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    [TestFixture]
    [MediumTest]
    public class OscGazeE2ETests : InputTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const int LoopbackPortBase = 19340;
        private const string ExpressionId = "gaze";
        private const float Tolerance = 0.06f;

        private static int s_portCounter;

        private readonly List<AdapterBindingBase> _startedBindings = new List<AdapterBindingBase>();
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();

        private InputSourceRegistry _registry;
        private FacialOutputBus _outputBus;
        private Gamepad _gamepad;

        public override void Setup()
        {
            base.Setup();
            _registry = new InputSourceRegistry();
            _outputBus = new FacialOutputBus();
        }

        public override void TearDown()
        {
            for (int i = _startedBindings.Count - 1; i >= 0; i--)
            {
                try
                {
                    _startedBindings[i]?.Dispose();
                }
                catch (Exception)
                {
                    // TearDown ではテスト本体の失敗を優先する。
                }
            }
            _startedBindings.Clear();

            if (_gamepad != null && _gamepad.added)
            {
                UnityEngine.InputSystem.InputSystem.RemoveDevice(_gamepad);
            }
            _gamepad = null;

            for (int i = _gameObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _gameObjects[i];
                if (go == null)
                {
                    continue;
                }

                go.SetActive(false);
                UnityEngine.Object.DestroyImmediate(go);
            }
            _gameObjects.Clear();

            for (int i = _objects.Count - 1; i >= 0; i--)
            {
                UnityEngine.Object obj = _objects[i];
                if (obj != null)
                {
                    UnityEngine.Object.DestroyImmediate(obj);
                }
            }
            _objects.Clear();

            _registry = null;
            _outputBus = null;

            base.TearDown();
        }

        [UnityTest]
        public IEnumerator Gaze_UdpLoopback_ReconstructsReceiverVector2()
        {
            int port = AllocatePort();
            var expected = new Vector2(0.64f, -0.37f);

            // 送信側の対応表の gaze チャネルだけで route を生成する。
            OscReceiverAdapterBinding receiver = CreateReceiver("vrchat-gaze-receiver", port);

            OscSenderAdapterBinding sender = CreateSender("vrchat-gaze-sender", port);

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_VRChatReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_VRChatSender")));

            yield return new WaitForSecondsRealtime(0.2f);

            yield return WaitUntilGazeVectorArrives(
                receiver,
                "vrchat-gaze-receiver:" + ExpressionId,
                expected,
                () =>
                {
                    _outputBus.Publish(
                        Array.Empty<float>(),
                        new[] { new GazeSnapshot(ExpressionId, expected.x, expected.y) });
                    sender.OnLateTick(0.016f);
                });
        }

        [UnityTest]
        public IEnumerator FacialController_DefaultGazeChannel_UdpLoopback_DrivesReceiverEyeBonesWithoutMapping()
        {
            int port = AllocatePort();
            const string receiverName = "OscGazeE2E_ControllerReceiver";

            var senderBinding = new OscSenderAdapterBinding
            {
                Slug = "gaze-e2e-sender",
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f,
            };
            senderBinding.ConfigureEndpoints(
                new[] { new OscSenderEndpointConfig(Endpoint, port, true) },
                Array.Empty<string>());
            senderBinding.ConfigureGazeChannels(new[] { GazeSourceIdConvention.DefaultChannelId });

            var receiverBinding = CreateReceiver("gaze-e2e-receiver", port);
            StartBinding(senderBinding, CreateContext(CreateGameObject("OscGazeE2E_GazeSender")));

            var senderProfile = CreateGazeProfile(
                new AdapterBindingBase[] { receiverBinding },
                providerSlug: string.Empty,
                name: receiverName);

            FacialController receiverController = CreateGazeController(receiverName, senderProfile, out Transform receiverLeftEye);
            yield return null;

            Assert.That(receiverController.IsInitialized, Is.True);

            Quaternion initialRotation = receiverLeftEye.localRotation;

            yield return new WaitForSecondsRealtime(0.2f);

            bool rotated = false;
            bool receiverValueRead = false;
            for (int attempt = 0; attempt < 20 && !rotated; attempt++)
            {
                _outputBus.Publish(
                    Array.Empty<float>(),
                    new[] { new GazeSnapshot(GazeSourceIdConvention.DefaultChannelId, 0.65f, 0.45f) });
                senderBinding.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);
                receiverBinding.OnFixedTick(0.02f);
                yield return null;
                if (receiverController.InputSourceRegistry.TryResolve(
                        "gaze-e2e-receiver:gaze", out IInputSource receiverSource)
                    && receiverSource is IAnalogInputSource receiverAnalog)
                {
                    receiverValueRead = receiverAnalog.TryReadVector2(out _, out _) || receiverValueRead;
                }
                rotated = Quaternion.Angle(initialRotation, receiverLeftEye.localRotation) > 0.1f;
            }

            Assert.That(receiverValueRead, Is.True,
                "受信側の自動登録済み gaze source が UDP 値を読み出せること。");
            Assert.That(rotated, Is.True,
                "送信側 Gaze セクション → 規約 id gaze → UDP loopback → 受信側自動登録 → 目ボーンの経路が成立すること。");

            receiverController.enabled = false;
        }

        [UnityTest]
        public IEnumerator FacialController_ReceiverWithoutGazeSetup_UsesSenderBonePathsAndRange()
        {
            int port = AllocatePort();
            const string receiverName = "OscGazeE2E_UnconfiguredReceiver";

            var senderBinding = new OscSenderAdapterBinding
            {
                Slug = "gaze-override-sender",
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f,
            };
            senderBinding.ConfigureEndpoints(
                new[] { new OscSenderEndpointConfig(Endpoint, port, true) },
                Array.Empty<string>());
            senderBinding.ConfigureGazeChannels(new[] { GazeSourceIdConvention.DefaultChannelId });
            // 送信側の目線タブ相当: Humanoid 以外の path と可動範囲を指定する。
            senderBinding.ConfigureGazeChannelSettings(new[]
            {
                new GazeChannel
                {
                    id = GazeSourceIdConvention.DefaultChannelId,
                    leftEyeBonePath = "Eyes/LeftEye",
                    rightEyeBonePath = "Eyes/RightEye",
                    lookUpAngle = 35f,
                    lookDownAngle = 20f,
                    outerYawAngle = 25f,
                    innerYawAngle = 22f,
                },
            });

            var receiverBinding = CreateReceiver("gaze-override-receiver", port);
            StartBinding(senderBinding, CreateContext(CreateGameObject("OscGazeE2E_OverrideSender")));

            // 受信側は目線タブを触らない (既定チャネル gaze のみ・path 未指定)。モデルは非 Humanoid なので、
            // 送信側の path が届かなければ目は動かない。
            var receiverProfile = ScriptableObject.CreateInstance<TestGazeProfileSO>();
            receiverProfile.name = receiverName + "Profile";
            receiverProfile.WritableAdapterBindings.Add(receiverBinding);
            _objects.Add(receiverProfile);
            GazeChannel receiverChannel = receiverProfile.GazeChannels[0];
            Assert.That(receiverChannel.leftEyeBonePath, Is.Empty);
            Assert.That(receiverChannel.lookUpAngle, Is.EqualTo(15f));

            FacialController receiverController = CreateGazeController(receiverName, receiverProfile, out Transform receiverLeftEye);
            yield return null;

            Assert.That(receiverController.IsInitialized, Is.True);
            Quaternion initialRotation = receiverLeftEye.localRotation;

            yield return new WaitForSecondsRealtime(0.2f);

            bool rotated = false;
            for (int attempt = 0; attempt < 20 && !rotated; attempt++)
            {
                _outputBus.Publish(
                    Array.Empty<float>(),
                    new[] { new GazeSnapshot(GazeSourceIdConvention.DefaultChannelId, 0f, 1f) });
                senderBinding.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);
                receiverBinding.OnFixedTick(0.02f);
                yield return null;
                rotated = Quaternion.Angle(initialRotation, receiverLeftEye.localRotation) > 0.1f;
            }

            Assert.That(rotated, Is.True,
                "送信側の目ボーン path が広告で届き、目線タブ未設定の受信側でも目ボーンが動くこと。");
            Assert.That(receiverBinding.TryGetGazeChannelOverride(
                    GazeSourceIdConvention.DefaultChannelId,
                    out GazeChannelOverride received),
                Is.True);
            Assert.That(received.LeftEyeBonePath, Is.EqualTo("Eyes/LeftEye"));
            Assert.That(received.LookUpAngle, Is.EqualTo(35f));
            // input (0, 1) は上方向。受信側ローカルの lookUp 15 ではなく、送信側の 35 で回る。
            // UDP 経由の値の誤差を見込み、ローカル値 15 と明確に区別できる範囲で確認する。
            float angle = Quaternion.Angle(initialRotation, receiverLeftEye.localRotation);
            Assert.That(angle, Is.GreaterThan(25f).And.LessThan(35.5f));

            receiverController.enabled = false;
        }

        [UnityTest]
        public IEnumerator Gaze_UdpLoopback_ResolvesSameVectorForBothEyes()
        {
            int port = AllocatePort();
            var expected = new Vector2(-0.42f, 0.58f);

            // 対応表の gaze チャネルから作った source が左右の目に同じ値を配る。
            OscReceiverAdapterBinding receiver = CreateReceiver("arkit-gaze-receiver", port);

            OscSenderAdapterBinding sender = CreateSender("arkit-gaze-sender", port);

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ARKitReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ARKitSender")));

            yield return new WaitForSecondsRealtime(0.2f);

            yield return WaitUntilResolvedGazeArrives(
                receiver,
                expected,
                expected,
                () =>
                {
                    _outputBus.Publish(
                        Array.Empty<float>(),
                        new[] { new GazeSnapshot(ExpressionId, expected.x, expected.y) });
                    sender.OnLateTick(0.016f);
                });
        }

        [Test]
        public void OldReceiverEquivalent_UnknownGazeAdvertisement_IsIgnoredWithoutLogs()
        {
            GameObject receiverObject = CreateGameObject("OscGazeE2E_OldReceiver");
            OscReceiver receiver = receiverObject.AddComponent<OscReceiver>();
            var buffer = new OscDoubleBuffer(0);
            try
            {
                receiver.Initialize(buffer, Array.Empty<OscMapping>());

                // 旧 receiver は広告 route を知らないが、通常の未知アドレスとして無警告で読み飛ばす。
                receiver.HandleOscMessage(new uOSC.Message(
                    "/_facialcontrol/gaze",
                    ExpressionId,
                    GazeAdvertisementResolver.VrChatXyFormat));
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                buffer.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_SameContentTwice_ReusesSources()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-reuse-receiver", port);
            OscSenderAdapterBinding sender = CreateSender("gaze-reuse-sender", port);

            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ReuseReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ReuseSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.GazeSources.Count, Is.EqualTo(1));
            GazeVector2InputSource original = receiver.GazeSources[0];
            uint originalHash = receiver.LastGazeAdvertisementHash;

            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.LastGazeAdvertisementHash, Is.EqualTo(originalHash));
            Assert.That(receiver.GazeSources.Count, Is.EqualTo(1));
            Assert.That(receiver.GazeSources[0], Is.SameAs(original));
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_ContentChanged_RebuildsAndUnregistersRemovedId()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-change-receiver", port);
            OscSenderAdapterBinding firstSender = CreateSender("gaze-change-sender-a", port);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ChangeReceiver")));
            StartBinding(firstSender, CreateContext(CreateGameObject("OscGazeE2E_ChangeSenderA")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, firstSender, ExpressionId);
            Assert.That(_registry.TryResolve("gaze-change-receiver:" + ExpressionId, out IInputSource removed), Is.True);
            int firstLayoutVersion = receiver.ActiveLayoutVersion;

            firstSender.Dispose();
            OscSenderAdapterBinding secondSender = CreateSenderWithIds(
                "gaze-change-sender-b", port, "eye-look-new");
            StartBinding(secondSender, CreateContext(CreateGameObject("OscGazeE2E_ChangeSenderB")));

            yield return SendGazeUntilProcessed(receiver, secondSender, "eye-look-new", firstLayoutVersion);

            Assert.That(receiver.AutoGazeSourceIds, Does.Contain("gaze-change-receiver:eye-look-new"));
            Assert.That(receiver.AutoGazeSourceIds, Does.Not.Contain("gaze-change-receiver:" + ExpressionId));
            Assert.That(_registry.TryResolve("gaze-change-receiver:" + ExpressionId, out _), Is.False);
            Assert.That(removed, Is.Not.Null);
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_Stops_PreservesRoutesAndSources()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-stop-receiver", port);
            OscSenderAdapterBinding sender = CreateSender("gaze-stop-sender", port);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_StopReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_StopSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);
            GazeVector2InputSource source = receiver.GazeSources[0];

            sender.Dispose();
            for (int i = 0; i < 4; i++)
            {
                receiver.OnFixedTick(0.02f);
                yield return null;
            }

            Assert.That(receiver.HasAutoGazeRoutes, Is.True);
            Assert.That(receiver.GazeSources[0], Is.SameAs(source));
            Assert.That(_registry.TryResolve("gaze-stop-receiver:" + ExpressionId, out IInputSource retained), Is.True);
            Assert.That(retained, Is.SameAs(source));
        }

        [UnityTest]
        public IEnumerator GazeAdvertisement_GazeConfigMatch_IsAcceptedForLateBoneConnection()
        {
            int port = AllocatePort();
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-config-receiver", port);
            receiver.ConfigureGazeChannels(new[] { ExpressionId });
            OscSenderAdapterBinding sender = CreateSender("gaze-config-sender", port);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_ConfigReceiver")));
            StartBinding(sender, CreateContext(CreateGameObject("OscGazeE2E_ConfigSender")));

            yield return new WaitForSecondsRealtime(0.2f);
            yield return SendGazeUntilProcessed(receiver, sender, ExpressionId);

            Assert.That(receiver.AutoGazeSourceIds, Does.Contain("gaze-config-receiver:" + ExpressionId));
        }

        [Test]
        public void NoLayout_NoRoutesNoErrorLog()
        {
            OscReceiverAdapterBinding receiver = CreateReceiver("gaze-empty-receiver", AllocatePort());
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_EmptyReceiver")));

            Assert.That(receiver.GazeSources, Is.Empty);
            Assert.That(receiver.HasAutoGazeRoutes, Is.False);
            LogAssert.NoUnexpectedReceived();
        }

        /// <summary>
        /// 送信側の対応表（gaze チャネルと属性を含む）が受信側に適用されるまで送る。
        /// <paramref name="previousLayoutVersion"/> を渡すと、それとは別の対応表が適用されるまで待つ。
        /// </summary>
        private IEnumerator SendGazeUntilProcessed(
            OscReceiverAdapterBinding receiver,
            OscSenderAdapterBinding sender,
            string expressionId,
            int previousLayoutVersion = OscFrameLayoutVersion.Unknown)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                _outputBus.Publish(
                    Array.Empty<float>(),
                    new[] { new GazeSnapshot(expressionId, 0.2f, -0.1f) });
                sender.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);
                if (receiver.ActiveLayoutVersion != OscFrameLayoutVersion.Unknown &&
                    receiver.ActiveLayoutVersion != previousLayoutVersion)
                {
                    yield break;
                }
            }

            Assert.Fail("gaze layout was not applied");
        }

        [UnityTest]
        public IEnumerator GazeLayout_AppliedThroughFacade_RegistersSharedSourceAndPublishesValue()
        {
            OscReceiverAdapterBinding receiver = CreateReceiver("osc-facade-gaze", AllocatePort());
            var oscSender = new SenderIdentity(Guid.NewGuid(), 1_000L);
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_FacadeGaze")));
            OscIndexedFrameMessages.ApplyLayout(
                receiver,
                oscSender,
                Array.Empty<string>(),
                new[] { new OscFrameLayoutGazeChannel(ExpressionId) });

            Assert.That(receiver.ActiveLayoutVersion, Is.EqualTo(OscIndexedFrameMessages.LayoutVersion));
            Assert.That(_registry.TryResolve("osc-facade-gaze:" + ExpressionId, out _), Is.True);

            OscIndexedFrameMessages.SendFrame(receiver.HelperHost.Receiver, oscSender, 2000UL, 0.3f, -0.4f);
            yield return new WaitForSecondsRealtime(0.05f);
            receiver.OnFixedTick(0.02f);

            Assert.That(TryReadVector("osc-facade-gaze:" + ExpressionId, out Vector2 actual), Is.True);
            Assert.That(actual.x, Is.EqualTo(0.3f).Within(Tolerance));
            Assert.That(actual.y, Is.EqualTo(-0.4f).Within(Tolerance));
        }

        [UnityTest]
        public IEnumerator GazeResolver_OscAndInputSystemSameExpressionId_SelectsLexicographicallyFirstSlug()
        {
            int port = AllocatePort();
            var inputValue = new Vector2(-0.55f, 0.25f);
            var oscValue = new Vector2(0.91f, -0.87f);

            OscReceiverAdapterBinding receiver = CreateReceiver("z-osc-gaze", port);
            InputSystemAdapterBinding inputBinding = CreateInputSystemGazeBinding("a-input-system-gaze");
            var oscSender = new SenderIdentity(Guid.NewGuid(), 1_000L);

            _gamepad = UnityEngine.InputSystem.InputSystem.AddDevice<Gamepad>();

            // OSC 側の gaze source（対応表の gaze チャネルから作る）を InputSystem 側より先に登録する。
            StartBinding(receiver, CreateContext(CreateGameObject("OscGazeE2E_DeterministicOsc")));
            OscIndexedFrameMessages.ApplyLayout(
                receiver,
                oscSender,
                Array.Empty<string>(),
                new[] { new OscFrameLayoutGazeChannel(ExpressionId) });
            StartBinding(inputBinding, CreateContext(CreateGameObject("OscGazeE2E_DeterministicInput")));

            yield return new WaitForSecondsRealtime(0.2f);

            using (UnityEngine.InputSystem.LowLevel.StateEvent.From(_gamepad, out var eventPtr))
            {
                _gamepad.leftStick.WriteValueIntoEvent(inputValue, eventPtr);
                UnityEngine.InputSystem.InputSystem.QueueEvent(eventPtr);
            }
            UnityEngine.InputSystem.InputSystem.Update();
            inputBinding.OnLateTick(0.016f);

            OscIndexedFrameMessages.SendFrame(receiver.HelperHost.Receiver, oscSender, 2000UL, oscValue.x, oscValue.y);
            yield return new WaitForSecondsRealtime(0.05f);
            receiver.OnFixedTick(0.02f);

            bool resolved = GazeChannelResolver.TryResolve(
                new GazeChannel { id = "gaze" },
                _registry,
                out ResolvedGazeInputSources sources);

            Assert.That(resolved, Is.True);
            Assert.That(sources.SelectedSlug, Is.EqualTo("a-input-system-gaze"));
            AssertVector(sources.LeftSource, inputValue, "InputSystem 側の Gaze source が採用されること。");
            AssertVector(sources.RightSource, inputValue, "InputSystem 側の Gaze source が両目に流用されること。");
        }

        private IEnumerator WaitUntilGazeVectorArrives(
            OscReceiverAdapterBinding receiver,
            string sourceId,
            Vector2 expected,
            Action send)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                send();
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                if (TryReadVector(sourceId, out Vector2 actual))
                {
                    Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance));
                    Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance));
                    yield break;
                }
            }

            Assert.Fail($"Gaze source '{sourceId}' に OSC loopback 値が到達しませんでした。");
        }

        private IEnumerator WaitUntilResolvedGazeArrives(
            OscReceiverAdapterBinding receiver,
            Vector2 expectedLeft,
            Vector2 expectedRight,
            Action send)
        {
            for (int attempt = 0; attempt < 20; attempt++)
            {
                send();
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                if (!GazeChannelResolver.TryResolve(
                        new GazeChannel { id = "gaze" },
                        _registry,
                        out ResolvedGazeInputSources sources))
                {
                    continue;
                }

                if (!TryReadVector(sources.LeftSource, out Vector2 actualLeft) ||
                    !TryReadVector(sources.RightSource, out Vector2 actualRight))
                {
                    continue;
                }

                Assert.That(actualLeft.x, Is.EqualTo(expectedLeft.x).Within(Tolerance));
                Assert.That(actualLeft.y, Is.EqualTo(expectedLeft.y).Within(Tolerance));
                Assert.That(actualRight.x, Is.EqualTo(expectedRight.x).Within(Tolerance));
                Assert.That(actualRight.y, Is.EqualTo(expectedRight.y).Within(Tolerance));
                yield break;
            }

            Assert.Fail("GazeChannel 既定解決経路で左右 Gaze source を読み取れませんでした。");
        }

        private OscReceiverAdapterBinding CreateReceiver(string slug, int port)
        {
            return new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                BundleMode = BundleInterpretationMode.AtomicSwap,
            };
        }

        private OscSenderAdapterBinding CreateSender(
            string slug,
            int port)
        {
            return CreateSenderWithIds(slug, port, ExpressionId);
        }

        private OscSenderAdapterBinding CreateSenderWithIds(
            string slug,
            int port,
            params string[] gazeExpressionIds)
        {
            var binding = new OscSenderAdapterBinding
            {
                Slug = slug,
                SuppressLoopback = false,
                HeartbeatIntervalSeconds = 60f,
            };
            binding.ConfigureEndpoints(
                new[]
                {
                    new OscSenderEndpointConfig(Endpoint, port, true)
                },
                Array.Empty<string>());
            binding.ConfigureGazeChannels(gazeExpressionIds);
            return binding;
        }

        private InputSystemAdapterBinding CreateInputSystemGazeBinding(string slug)
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _objects.Add(asset);

            InputActionMap map = asset.AddActionMap("Expression");
            map.AddAction("GazeLook", InputActionType.Value, expectedControlLayout: "Vector2")
                .AddBinding("<Gamepad>/leftStick");

            var binding = new InputSystemAdapterBinding
            {
                Slug = slug,
            };
            binding.Configure(
                asset,
                "Expression",
                new[]
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = BindingMode.Gaze,
                        expressionId = ExpressionId,
                        actionName = "GazeLook",
                    }
                });
            binding.ConfigureGazeChannels(new[] { ExpressionId });
            return binding;
        }

        private TestGazeProfileSO CreateGazeProfile(
            IReadOnlyList<AdapterBindingBase> bindings,
            string providerSlug,
            string name)
        {
            var profile = ScriptableObject.CreateInstance<TestGazeProfileSO>();
            profile.name = name + "Profile";
            profile.AddGazeChannel(new GazeChannel
            {
                id = GazeSourceIdConvention.DefaultChannelId,
                providerSlug = providerSlug,
                leftEyeBonePath = "Eyes/LeftEye",
                rightEyeBonePath = "Eyes/RightEye",
                lookUpAngle = 30f,
                lookDownAngle = 30f,
                outerYawAngle = 30f,
                innerYawAngle = 30f,
            });
            for (int i = 0; i < bindings.Count; i++)
            {
                profile.WritableAdapterBindings.Add(bindings[i]);
            }
            _objects.Add(profile);
            return profile;
        }

        private FacialController CreateGazeController(
            string name,
            TestGazeProfileSO profile,
            out Transform leftEye)
        {
            GameObject root = CreateGameObject(name);
            root.AddComponent<Animator>();
            GameObject eyes = new GameObject("Eyes");
            eyes.transform.SetParent(root.transform);
            leftEye = new GameObject("LeftEye").transform;
            leftEye.SetParent(eyes.transform);
            Transform rightEye = new GameObject("RightEye").transform;
            rightEye.SetParent(eyes.transform);
            Mesh mesh = new Mesh { name = name + "Mesh" };
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            _objects.Add(mesh);
            var renderer = new GameObject("Mesh").AddComponent<SkinnedMeshRenderer>();
            renderer.transform.SetParent(root.transform);
            renderer.sharedMesh = mesh;
            var controller = root.AddComponent<FacialController>();
            controller.CharacterSO = profile;
            controller.SkinnedMeshRenderers = new[] { renderer };
            controller.Initialize();
            return controller;
        }

        private sealed class TestGazeProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public void AddGazeChannel(GazeChannel channel)
            {
                var channels = (List<GazeChannel>)GazeChannels;
                channels.Clear();
                channels.Add(channel);
            }
        }

        private AdapterBuildContext CreateContext(GameObject host)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("2.0"),
                blendShapeNames: Array.Empty<string>(),
                inputSourceRegistry: _registry,
                facialOutputBus: _outputBus,
                timeProvider: new UnityTimeProvider(),
                hostGameObject: host,
                lipSyncProvider: null);
        }

        private GameObject CreateGameObject(string name)
        {
            var go = new GameObject(name);
            _gameObjects.Add(go);
            return go;
        }

        private void StartBinding(AdapterBindingBase binding, AdapterBuildContext context)
        {
            binding.OnStart(in context);
            _startedBindings.Add(binding);
        }

        private bool TryReadVector(string sourceId, out Vector2 value)
        {
            value = default;
            if (!_registry.TryResolve(sourceId, out IInputSource source) ||
                source is not IAnalogInputSource analog)
            {
                return false;
            }

            return TryReadVector(analog, out value);
        }

        private static bool TryReadVector(IAnalogInputSource source, out Vector2 value)
        {
            value = default;
            if (source == null || !source.IsValid)
            {
                return false;
            }

            if (!source.TryReadVector2(out float x, out float y))
            {
                return false;
            }

            value = new Vector2(x, y);
            return true;
        }

        private static void AssertVector(
            IAnalogInputSource source,
            Vector2 expected,
            string message)
        {
            Assert.That(TryReadVector(source, out Vector2 actual), Is.True, message);
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Tolerance), message);
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Tolerance), message);
        }

        private static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }
    }
}
#endif
