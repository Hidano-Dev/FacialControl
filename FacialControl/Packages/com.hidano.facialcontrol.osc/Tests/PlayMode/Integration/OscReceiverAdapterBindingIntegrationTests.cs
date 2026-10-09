using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// <see cref="OscReceiverAdapterBinding"/> の PlayMode 統合テスト (host GameObject + <see cref="InputSourceRegistry"/> 前提)。
    /// <c>OnStart</c> による <see cref="OscReceiverHost"/> の AddComponent と slug 登録、実 UDP loopback での値到達、
    /// 診断 API と Dispose による runtime 状態解放、Gaze VRChat_XY バンドル経路、<c>Dispose</c> による helper 破棄と socket 解放を検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscReceiverAdapterBindingIntegrationTests : SizedTestFixture
    {
        private const string TestEndpoint = "127.0.0.1";

        private GameObject _hostGameObject;
        private GameObject _senderGameObject;
        private InputSourceRegistry _registry;
        private OscReceiverAdapterBinding _binding;
        private bool _bindingStarted;

        [SetUp]
        public void SetUp()
        {
            _registry = new InputSourceRegistry();
            _hostGameObject = new GameObject("OscReceiverAdapterBindingIntegrationTestsHost");
            _bindingStarted = false;
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                try
                {
                    _binding.Dispose();
                }
                catch (Exception)
                {
                    // TearDown では例外を握り潰し、テスト本体の assertion を優先する。
                }
            }
            _binding = null;
            _bindingStarted = false;

            if (_senderGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_senderGameObject);
                _senderGameObject = null;
            }
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        // ---------------------------------------------------------------
        // OnStart / OnFixedTick: 空 mapping と診断 API
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator OnStart_EmptyMappings_StartsSocketWithEmptyPrimaryInputSource()
        {
            const string slug = "osc-empty-socket";
            int port = AllocatePort();
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                Mappings = new List<OscMappingEntry>()
            };
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            yield return new WaitForSeconds(0.2f);

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.HelperHost, Is.Not.Null);
            Assert.That(_binding.HelperHost.IsConfigured, Is.True);
            Assert.That(_binding.HelperHost.Receiver, Is.Not.Null);
            Assert.That(_binding.HelperHost.Receiver.IsRunning, Is.True);
            Assert.That(_binding.Buffer, Is.Not.Null);
            Assert.That(_binding.Buffer.Size, Is.EqualTo(0));
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_registry.TryResolve(slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(_binding.InputSource));
            AssertMask(source.ContributeMask, false, false);
        }

        [Test]
        public void DiagnosticApis_ExposeManualOriginsThenDisposeClearsRuntimeState()
        {
            const string slug = "osc-diagnostic-runtime-state";
            int port = AllocatePort();
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(2));
            Assert.That(_binding.GetMappingOrigin(0),
                Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.Manual));
            Assert.That(_binding.GetMappingOrigin(1),
                Is.EqualTo(OscReceiverAdapterBinding.MappingOrigin.Manual));
            Assert.Throws<ArgumentOutOfRangeException>(() => _binding.GetMappingOrigin(2));

            _binding.Dispose();
            _bindingStarted = false;

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(0));
            Assert.That(_binding.MappingOrigins.Count, Is.EqualTo(0));
            Assert.That(_binding.Buffer, Is.Null);
            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_binding.IsStarted, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => _binding.GetMappingOrigin(0));
        }

        // ---------------------------------------------------------------
        // OnStart: helper AddComponent + InputSourceRegistry 登録
        // ---------------------------------------------------------------

        [Test]
        public void OnStart_AddsOscReceiverHostHelperToContextHostGameObject()
        {
            int port = AllocatePort();
            OscMapping[] mappings = CreateDefaultMappings();
            _binding = CreateBinding(slug: "osc-helper-add", endpoint: TestEndpoint, port: port, mappings: mappings);

            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscReceiverHost helper = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNotNull(helper,
                "OnStart は ctx.HostGameObject に OscReceiverHost を AddComponent するべき。");
        }

        [Test]
        public void OnStart_HelperHostHideFlags_DoesNotIncludeHideInInspector()
        {
            int port = AllocatePort();
            _binding = CreateBinding(slug: "osc-helper-hideflags", endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscReceiverHost helper = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNotNull(helper);

            HideFlags actualFlags = helper.hideFlags;
            Assert.That((actualFlags & HideFlags.HideInInspector), Is.EqualTo(HideFlags.None),
                "helper MonoBehaviour は Inspector で見える（HideInInspector を含まない）べき。");
        }

        [Test]
        public void OnStart_RegistersPrimaryInputSourceUnderSlug()
        {
            const string slug = "osc-primary-resolve";
            int port = AllocatePort();
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            bool resolved = _registry.TryResolve(slug, out IInputSource source);
            Assert.IsTrue(resolved,
                $"InputSourceRegistry.TryResolve(\"{slug}\") は OnStart 後に true を返すべき。");
            Assert.IsNotNull(source,
                "解決結果の IInputSource は non-null であるべき。");
        }

        // ---------------------------------------------------------------
        // OnStart: 手動 mapping の mesh index ContributeMask / Gaze バンドル / preset
        // ---------------------------------------------------------------

        [Test]
        public void OnStart_ManualBlendShapeMappings_RegistersPrimarySourceWithMeshIndexContributeMask()
        {
            const string slug = "osc-manual-mask-regression";
            int port = AllocatePort();
            OscMapping[] mappings = new[]
            {
                new OscMapping("/avatar/parameters/frown", "frown", "emotion"),
                new OscMapping("/avatar/parameters/smile", "smile", "emotion")
            };
            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port, mappings: mappings);
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "blink", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.IsStarted, Is.True);
            Assert.That(_binding.Buffer, Is.Not.Null);
            Assert.That(_binding.Buffer.Size, Is.EqualTo(2));
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_registry.TryResolve(slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(_binding.InputSource));
            AssertMask(source.ContributeMask, true, false, true);

            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message("/avatar/parameters/frown", 0.75f));
            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message("/avatar/parameters/smile", 0.25f));
            _binding.OnFixedTick(0.02f);

            var output = new float[] { -1f, -1f, -1f };
            Assert.That(source.TryWriteValues(output), Is.True);
            Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
            Assert.That(output[1], Is.EqualTo(-1f).Within(1e-6f));
            Assert.That(output[2], Is.EqualTo(0.75f).Within(1e-6f));
        }

        [Test]
        public void OnFixedTick_GazeVrchatBundleOnly_UsesAccumulatorWithoutPrimaryOscInputSource()
        {
            const string slug = "osc-gaze-bundle-regression";
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            _binding = new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                BundleAccumulationTimeoutMs = 5f,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "eye",
                        addressPattern = "/avatar/parameters/eye",
                    }
                }
            };
            AdapterBuildContext ctx = CreateContext(timeProvider: time);

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_registry.TryResolve(slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(_binding.InputSource));
            Assert.That(_registry.TryResolve(slug + ":eye", out IInputSource inputSource), Is.True);
            Assert.That(inputSource, Is.InstanceOf<GazeVector2InputSource>());
            AssertMask(inputSource.ContributeMask);

            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage("/avatar/parameters/eyeX", 0.2f, timestamp: 100UL));
            _binding.HelperHost.Receiver.HandleOscMessage(
                FloatMessage("/avatar/parameters/eyeY", -0.6f, timestamp: 100UL));
            _binding.OnFixedTick(0.02f);

            var gaze = (GazeVector2InputSource)inputSource;
            Assert.That(gaze.TryReadVector2(out _, out _), Is.False);

            time.UnscaledTimeSeconds = 0.006;
            _binding.OnFixedTick(0.02f);

            Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(y, Is.EqualTo(-0.6f).Within(1e-6f));
        }

        // ---------------------------------------------------------------
        // OnStart: 実 UDP loopback でメッセージが registered InputSource に到達する
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator OnStart_UdpLoopback_RegisteredInputSourceReceivesValue()
        {
            const string slug = "osc-loopback";
            int port = AllocatePort();
            OscMapping[] mappings = new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion")
            };

            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port, mappings: mappings);
            AdapterBuildContext ctx = CreateContext(blendShapeNames: new List<string> { "smile", "frown" });

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            // socket bind 待ち
            yield return new WaitForSeconds(0.2f);

            _senderGameObject = new GameObject("OscAdapterBindingIntegrationSender");
            OscSender sender = _senderGameObject.AddComponent<OscSender>();
            sender.Endpoint = TestEndpoint;
            sender.Port = port;
            sender.Initialize(mappings);
            sender.StartSending();

            yield return new WaitForSeconds(0.2f);

            Assert.IsTrue(_registry.TryResolve(slug, out IInputSource source));
            Assert.IsNotNull(source);

            float[] readBuffer = new float[mappings.Length];
            bool received = false;
            for (int attempt = 0; attempt < 10 && !received; attempt++)
            {
                sender.SendAll(new float[] { 0.7f, 0.3f });
                yield return new WaitForSeconds(0.1f);
                _binding.OnFixedTick(0.02f);

                Array.Clear(readBuffer, 0, readBuffer.Length);
                if (TryReadValues(source, readBuffer) && readBuffer[0] > 0.01f)
                {
                    received = true;
                    Assert.That(readBuffer[0], Is.EqualTo(0.7f).Within(0.05f),
                        "Loopback 送信値（smile = 0.7）が registered InputSource から読めるべき。");
                    Assert.That(readBuffer[1], Is.EqualTo(0.3f).Within(0.05f),
                        "Loopback 送信値（frown = 0.3）が registered InputSource から読めるべき。");
                }
            }

            sender.StopSending();
            Assert.IsTrue(received,
                "実 UDP loopback で送信した値が registered InputSource に届くべき。");
        }

        // ---------------------------------------------------------------
        // Dispose: helper destroy + socket close
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Dispose_DestroysOscReceiverHostHelper()
        {
            int port = AllocatePort();
            _binding = CreateBinding(slug: "osc-dispose-destroy", endpoint: TestEndpoint, port: port,
                mappings: CreateDefaultMappings());
            AdapterBuildContext ctx = CreateContext();

            _binding.OnStart(in ctx);
            _bindingStarted = true;

            OscReceiverHost helper = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNotNull(helper, "OnStart 後は helper が AddComponent されているはず。");

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;

            // Unity の MonoBehaviour Object 等価性: Destroy 後の参照は == null となる。
            Assert.IsTrue(helper == null,
                "Dispose 時に Object.Destroy(_helperHost) で helper が破棄されるべき。");

            OscReceiverHost remaining = _hostGameObject.GetComponent<OscReceiverHost>();
            Assert.IsNull(remaining,
                "Dispose 後の Host GameObject から OscReceiverHost が剥がれているべき。");
        }

        [UnityTest]
        public IEnumerator Dispose_ClosesUdpSocket_NewBindingCanRebindSamePort()
        {
            const string slug = "osc-dispose-socket";
            int port = AllocatePort();
            OscMapping[] mappings = CreateDefaultMappings();

            _binding = CreateBinding(slug: slug, endpoint: TestEndpoint, port: port, mappings: mappings);
            AdapterBuildContext ctx = CreateContext();
            _binding.OnStart(in ctx);
            _bindingStarted = true;

            yield return new WaitForSeconds(0.2f);

            _binding.Dispose();
            _bindingStarted = false;

            yield return null;
            yield return new WaitForSeconds(0.2f);

            // 同 port を新規 binding で再 bind できれば socket は close されている。
            var second = CreateBinding(slug: slug + "-2", endpoint: TestEndpoint, port: port, mappings: mappings);
            var secondContext = CreateContext();
            Assert.DoesNotThrow(() => second.OnStart(in secondContext),
                "Dispose 後は同 port を別 binding で再 bind できるべき（socket 解放）。");

            try
            {
                yield return new WaitForSeconds(0.1f);
            }
            finally
            {
                second.Dispose();
            }
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private OscReceiverAdapterBinding CreateBinding(string slug, string endpoint, int port, OscMapping[] mappings)
        {
            var binding = new OscReceiverAdapterBinding();
            binding.Slug = slug;
            binding.Configure(endpoint, port, mappings);
            return binding;
        }

        private AdapterBuildContext CreateContext(
            IReadOnlyList<string> blendShapeNames = null,
            ITimeProvider timeProvider = null)
        {
            return new AdapterBuildContext(
                profile: new FacialProfile("1.0"),
                blendShapeNames: blendShapeNames ?? new List<string> { "smile", "frown" },
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: timeProvider ?? new UnityTimeProvider(),
                hostGameObject: _hostGameObject,
                lipSyncProvider: null);
        }

        private static int AllocatePort()
        {
            return OscReceiverAdapterBindingTestSupport.AllocatePort();
        }

        private static OscMapping[] CreateDefaultMappings()
        {
            return new OscMapping[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
                new OscMapping("/avatar/parameters/frown", "frown", "emotion")
            };
        }

        private static bool TryReadValues(IInputSource source, float[] buffer)
        {
            return source.TryWriteValues(buffer.AsSpan());
        }

        private static uOSC.Message FloatMessage(string address, float value, ulong timestamp)
        {
            var message = new uOSC.Message(address, value);
            message.timestamp = new uOSC.Timestamp(timestamp);
            return message;
        }

        private static void AssertMask(BitArray mask, params bool[] expected)
        {
            Assert.That(mask.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]), $"mask[{i}]");
            }
        }
    }

    /// <summary>
    /// <see cref="OscReceiverAdapterBinding"/> の PlayMode 統合テスト (BlendShape 付き <see cref="SkinnedMeshRenderer"/> 前提)。
    /// 空 mapping では旧送信側の heartbeat が届いても renderer を動かさないこと、
    /// VRChat_XY 左右独立指定の警告、Dispose による runtime 状態解放を検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscReceiverAdapterBindingWithMeshRendererTests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const string Slug = "osc-auto-mapping";

        private GameObject _host;
        private GameObject _meshObject;
        private Mesh _mesh;
        private SkinnedMeshRenderer _renderer;
        private InputSourceRegistry _registry;
        private OscReceiverAdapterBinding _binding;
        private bool _bindingStarted;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("OscReceiverAutoMappingHost");
            _registry = new InputSourceRegistry();
        }

        [TearDown]
        public void TearDown()
        {
            if (_binding != null && _bindingStarted)
            {
                _binding.Dispose();
            }

            _binding = null;
            _bindingStarted = false;

            if (_meshObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_meshObject);
                _meshObject = null;
            }

            if (_host != null)
            {
                UnityEngine.Object.DestroyImmediate(_host);
                _host = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        [UnityTest]
        public IEnumerator OnStart_EmptyMappingsAndLegacyHeartbeat_KeepsEmptyInputSourceAndDoesNotChangeRenderer()
        {
            StartBindingWithMesh("smile");

            yield return null;
            // 旧送信側の heartbeat が届いても、名前の積集合から mapping を作らない。
            _binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message("/_facialcontrol/blendshape_names", "smile"));
            _binding.OnFixedTick(0.02f);
            SendOscValue("/avatar/parameters/smile", 0.9f);
            _binding.OnFixedTick(0.02f);

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(0));
            Assert.That(_binding.InputSource, Is.Not.Null);
            Assert.That(_registry.TryResolve(Slug, out IInputSource source), Is.True);
            Assert.That(source, Is.SameAs(_binding.InputSource));
            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.01f));
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void OnStart_VrChatXyLeftRightIndependent_LogsRuntimeWarningOnceAndKeepsRoute()
        {
            var mappings = new[]
            {
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = "look",
                    addressPattern = "/avatar/parameters/look",
                    leftRightIndependent = true,
                    sourceIdLeft = "look.left",
                    sourceIdRight = "look.right"
                },
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = "look2",
                    addressPattern = "/avatar/parameters/look2",
                    leftRightIndependent = true,
                    sourceIdLeft = "look2.left",
                    sourceIdRight = "look2.right"
                }
            };

            LogAssert.Expect(
                LogType.Warning,
                new System.Text.RegularExpressions.Regex("VRChat_XY.*Vector2.*左右には同値"));
            StartBindingWithMesh(mappings, "smile");

            Assert.That(_registry.TryResolve(Slug + ":look.left", out _), Is.True);
            Assert.That(_registry.TryResolve(Slug + ":look.right", out _), Is.True);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Dispose_ManualMappingsAllocated_ReleasesRuntimeState()
        {
            StartBindingWithMesh(
                new[]
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = "smile",
                        addressPattern = "/manual/smile"
                    }
                },
                "smile");
            SendOscValue("/manual/smile", 0.25f);
            _binding.OnFixedTick(0.02f);
            ApplyRegisteredSourceToRenderer();

            Assert.That(_renderer.GetBlendShapeWeight(0), Is.EqualTo(25f).Within(0.01f));

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(1));
            Assert.That(_binding.InputSource, Is.Not.Null);

            _binding.Dispose();
            _bindingStarted = false;

            Assert.That(_binding.RuntimeMappings.Count, Is.EqualTo(0));
            Assert.That(_binding.MappingOrigins.Count, Is.EqualTo(0));
            Assert.That(_binding.InputSource, Is.Null);
            Assert.That(_binding.Buffer, Is.Null);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private void StartBindingWithMesh(params string[] blendShapeNames)
        {
            StartBindingWithMesh(Array.Empty<OscMappingEntry>(), blendShapeNames);
        }

        private void StartBindingWithMesh(IReadOnlyList<OscMappingEntry> mappings, params string[] blendShapeNames)
        {
            CreateRenderer(blendShapeNames);
            _binding = new OscReceiverAdapterBinding
            {
                Slug = Slug,
                Port = OscReceiverAdapterBindingTestSupport.AllocatePort(),
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>(mappings)
            };

            AdapterBuildContext ctx = new AdapterBuildContext(
                profile: CreateProfile(),
                blendShapeNames: blendShapeNames,
                inputSourceRegistry: _registry,
                facialOutputBus: new FacialOutputBus(),
                timeProvider: new UnityTimeProvider(),
                hostGameObject: _host,
                lipSyncProvider: null);

            _binding.OnStart(in ctx);
            _bindingStarted = true;
            Assert.That(_binding.IsStarted, Is.True);
        }

        private void CreateRenderer(params string[] blendShapeNames)
        {
            _mesh = new Mesh { name = "OscReceiverAutoMappingMesh" };
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };

            var deltas = new[] { Vector3.zero, Vector3.zero, Vector3.zero };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                _mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, deltas, null, null);
            }

            _meshObject = new GameObject("OscReceiverAutoMappingRenderer");
            _renderer = _meshObject.AddComponent<SkinnedMeshRenderer>();
            _renderer.sharedMesh = _mesh;
        }

        private void SendOscValue(string address, float value)
        {
            _binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message(address, value));
        }

        private void ApplyRegisteredSourceToRenderer()
        {
            Assert.That(_registry.TryResolve(Slug, out IInputSource source), Is.True);
            var values = new float[_renderer.sharedMesh.blendShapeCount];
            Assert.That(source.TryWriteValues(values), Is.True);

            for (int i = 0; i < values.Length; i++)
            {
                _renderer.SetBlendShapeWeight(i, values[i] * 100f);
            }
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                "2.0",
                new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
                },
                expressions: null,
                rendererPaths: null,
                layerInputSources: new[]
                {
                    new[]
                    {
                        new InputSourceDeclaration(Slug, 1f, null)
                    }
                });
        }
    }

    /// <summary>
    /// <see cref="OscReceiverAdapterBinding"/> / <see cref="OscSenderAdapterBinding"/> の PlayMode 統合テスト
    /// (受信ポート・送信先を binding 本体に、上級設定を任意の SO に持たせる構成)。
    /// 上級設定アセットなしでもポートだけで受信できること、binding の送信先へ実 UDP で送信できること、
    /// 未移行の旧 <see cref="OscRuntimeSettingsSO"/> を割り当てたままでも従来どおり送受信できることを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscReceiverAdapterBindingWithRuntimeSettingsTests : SizedTestFixture
    {
        private const string Endpoint = "127.0.0.1";
        private const string BlendShapeNameA = "smile";
        private const string BlendShapeNameB = "frown";
        private const float Tolerance = 0.05f;

        private readonly List<AdapterBindingBase> _startedBindings = new List<AdapterBindingBase>();
        private readonly List<GameObject> _gameObjects = new List<GameObject>();
        private readonly List<ScriptableObject> _settingsAssets = new List<ScriptableObject>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _startedBindings.Count - 1; i >= 0; i--)
            {
                try
                {
                    _startedBindings[i]?.Dispose();
                }
                catch (Exception)
                {
                    // TearDown 中の例外は無視して assertion を優先する。
                }
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

            for (int i = _settingsAssets.Count - 1; i >= 0; i--)
            {
                ScriptableObject so = _settingsAssets[i];
                if (so != null)
                {
                    UnityEngine.Object.DestroyImmediate(so);
                }
            }
            _settingsAssets.Clear();
        }

        // ---------------------------------------------------------------
        // Receiver: 上級設定アセットなし・ポートだけで UDP loopback メッセージを受信できる
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Receiver_PortOnlyWithoutAdvancedAsset_ReceivesUdpLoopbackValue()
        {
            const string slug = "osc-receiver-port-only";
            int port = AllocatePort();

            OscReceiverAdapterBinding receiver = CreateReceiver(slug, port, advancedSettings: null);
            GameObject receiverHost = CreateGameObject("OscReceiverAdapterBindingWithRuntimeSettingsTests_Receiver");
            var registry = new InputSourceRegistry();

            StartBinding(receiver, CreateContext(registry, new FacialOutputBus(), receiverHost,
                new[] { BlendShapeNameA, BlendShapeNameB }));

            Assert.That(receiver.IsStarted, Is.True,
                "OscReceiverAdapterBinding は上級設定アセットなしでも起動できるべき。");
            Assert.That(receiver.AdvancedSettings, Is.Null);
            Assert.That(receiver.HelperHost, Is.Not.Null,
                "OnStart 後は OscReceiverHost が AddComponent されているべき。");
            Assert.That(receiver.HelperHost.Port, Is.EqualTo(port),
                "binding の受信ポートが OscReceiverHost.Configure に伝播するべき。");

            yield return new WaitForSecondsRealtime(0.2f);

            GameObject senderHost = CreateGameObject("OscReceiverAdapterBindingWithRuntimeSettingsTests_RawSender");
            OscSender sender = senderHost.AddComponent<OscSender>();
            sender.Endpoint = Endpoint;
            sender.Port = port;
            OscMapping[] mappings = new[]
            {
                new OscMapping(OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameA, BlendShapeNameA, "emotion"),
                new OscMapping(OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameB, BlendShapeNameB, "emotion"),
            };
            sender.Initialize(mappings);
            sender.StartSending();

            yield return new WaitForSecondsRealtime(0.2f);

            Assert.That(registry.TryResolve(slug, out IInputSource source), Is.True);

            float[] readBuffer = new float[mappings.Length];
            bool received = false;
            for (int attempt = 0; attempt < 20 && !received; attempt++)
            {
                sender.SendAll(new[] { 0.62f, 0.31f });
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                Array.Clear(readBuffer, 0, readBuffer.Length);
                if (source.TryWriteValues(readBuffer.AsSpan()) && readBuffer[0] > 0.01f)
                {
                    received = true;
                    Assert.That(readBuffer[0], Is.EqualTo(0.62f).Within(Tolerance));
                    Assert.That(readBuffer[1], Is.EqualTo(0.31f).Within(Tolerance));
                }
            }

            sender.StopSending();
            Assert.That(received, Is.True,
                "binding の受信ポートで bind した OscReceiver が UDP loopback 値を受信できるべき。");
        }

        // ---------------------------------------------------------------
        // Sender: binding の送信先へ UDP 送信が行われる（上級設定アセットの値も反映される）
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator Sender_BindingEndpoints_SendsBlendShapeValueOverUdp()
        {
            int port = AllocatePort();

            OscSenderRuntimeSettingsSO advanced = CreateSenderAdvancedSettings(
                "{\"heartbeatIntervalSeconds\":60,\"suppressLoopback\":false}");
            OscSenderAdapterBinding sender = CreateSender("osc-sender-binding-endpoints", port, advanced,
                new[] { BlendShapeNameA });
            GameObject senderHost = CreateGameObject("OscReceiverAdapterBindingWithRuntimeSettingsTests_Sender");
            var outputBus = new FacialOutputBus();

            // 受信側は生の UDP ソケットで値フレーム（/_facialcontrol/values）を読んで値を観測する。
            using (var rawReceiver = new System.Net.Sockets.UdpClient(
                       new System.Net.IPEndPoint(System.Net.IPAddress.Loopback, port)))
            {
                yield return new WaitForSecondsRealtime(0.2f);

                StartBinding(sender, CreateContext(new InputSourceRegistry(), outputBus, senderHost,
                    new[] { BlendShapeNameA }));

                Assert.That(sender.IsStarted, Is.True,
                    "OscSenderAdapterBinding は binding の送信先で起動できるべき。");
                Assert.That(sender.HelperSenderCount, Is.EqualTo(1),
                    "送信先の件数だけ OscSender が AddComponent されるべき。");
                Assert.That(sender.GetHelperSender(0).Port, Is.EqualTo(port),
                    "送信先の port が OscSender.Configure に伝播するべき。");
                Assert.That(sender.HeartbeatIntervalSeconds, Is.EqualTo(60f),
                    "上級設定アセットの値が反映されるべき。");

                yield return new WaitForSecondsRealtime(0.2f);

                bool received = false;
                float[] postBlendValues = new[] { 0.81f };
                var slots = new float[1];
                for (int attempt = 0; attempt < 20 && !received; attempt++)
                {
                    outputBus.Publish(postBlendValues, ReadOnlySpan<GazeSnapshot>.Empty);
                    sender.OnLateTick(0.016f);
                    yield return new WaitForSecondsRealtime(0.05f);

                    while (rawReceiver.Available > 0)
                    {
                        System.Net.IPEndPoint remote = null;
                        received |= TryReadSingleValue(rawReceiver.Receive(ref remote), slots);
                    }
                }

                Assert.That(received, Is.True,
                    "binding の送信先で起動した OscSender が UDP 経由で値フレームを送信するべき。");
                Assert.That(slots[0], Is.EqualTo(0.81f).Within(Tolerance));
            }
        }

        /// <summary>データグラムに値 1 つの値フレームがあれば <paramref name="slots"/> に写して true。</summary>
        private static bool TryReadSingleValue(byte[] datagram, float[] slots)
        {
            var reader = new OscPacketReader(datagram);
            while (reader.TryReadNext(out OscMessageView view))
            {
                if (OscIndexedFrameCodec.IsValuesAddress(view.Address)
                    && OscIndexedFrameCodec.TryCopyValues(in view, slots, out int written)
                    && written == 1)
                {
                    return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------
        // Legacy SO: 未移行の旧 OscRuntimeSettingsSO を Receiver と Sender に割り当てたままでも、
        // その listen / send port で従来どおり送受信できる。
        // ---------------------------------------------------------------

        [UnityTest]
        public IEnumerator LegacySharedSettings_ReceiverAndSender_StillExchangeValueOverUdp()
        {
            int port = AllocatePort();

            OscRuntimeSettingsSO shared = CreateLegacySettings(BuildLegacySharedJson(port));
            OscReceiverAdapterBinding receiver = CreateReceiver("osc-receiver-shared", AllocatePort(), advancedSettings: null);
            receiver.LegacySettings = shared;
            OscSenderAdapterBinding sender = CreateSender("osc-sender-shared", AllocatePort(), advancedSettings: null,
                new[] { BlendShapeNameA });
            sender.LegacySettings = shared;

            GameObject receiverHost = CreateGameObject("OscReceiverAdapterBindingWithRuntimeSettingsTests_SharedReceiver");
            GameObject senderHost = CreateGameObject("OscReceiverAdapterBindingWithRuntimeSettingsTests_SharedSender");

            var registry = new InputSourceRegistry();
            var outputBus = new FacialOutputBus();

            StartBinding(receiver, CreateContext(registry, new FacialOutputBus(), receiverHost,
                new[] { BlendShapeNameA }));
            yield return new WaitForSecondsRealtime(0.2f);
            StartBinding(sender, CreateContext(new InputSourceRegistry(), outputBus, senderHost,
                new[] { BlendShapeNameA }));

            Assert.That(receiver.IsStarted, Is.True);
            Assert.That(sender.IsStarted, Is.True);
            Assert.That(receiver.HelperHost.Port, Is.EqualTo(port),
                "旧設定の ListenPort が binding 側のポートより優先されること。");
            Assert.That(sender.GetHelperSender(0).Port, Is.EqualTo(port),
                "旧設定の Endpoints[0].port が binding 側の送信先より優先されること。");

            yield return new WaitForSecondsRealtime(0.2f);

            float[] postBlendValues = new[] { 0.47f };
            float[] readBuffer = new float[1];
            bool reached = false;

            for (int attempt = 0; attempt < 20 && !reached; attempt++)
            {
                outputBus.Publish(postBlendValues, ReadOnlySpan<GazeSnapshot>.Empty);
                sender.OnLateTick(0.016f);
                yield return new WaitForSecondsRealtime(0.05f);
                receiver.OnFixedTick(0.02f);

                if (!registry.TryResolve("osc-receiver-shared", out IInputSource source) || source == null)
                {
                    continue;
                }

                Array.Clear(readBuffer, 0, readBuffer.Length);
                if (source.TryWriteValues(readBuffer.AsSpan()) && readBuffer[0] > 0.01f)
                {
                    reached = true;
                    Assert.That(readBuffer[0], Is.EqualTo(0.47f).Within(Tolerance),
                        "旧設定の listen/send port を介して Sender 出力が Receiver に到達するべき。");
                }
            }

            Assert.That(reached, Is.True,
                "未移行の旧 OscRuntimeSettingsSO を割り当てたままでも、その port 経由で値が送受信できるべき。");
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private OscRuntimeSettingsSO CreateLegacySettings(string json)
        {
            var settings = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.FromJson(json);
            _settingsAssets.Add(settings);
            return settings;
        }

        private OscSenderRuntimeSettingsSO CreateSenderAdvancedSettings(string json)
        {
            var settings = ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
            settings.hideFlags = HideFlags.HideAndDontSave;
            settings.FromJson(json);
            _settingsAssets.Add(settings);
            return settings;
        }

        private OscReceiverAdapterBinding CreateReceiver(
            string slug,
            int port,
            OscReceiverRuntimeSettingsSO advancedSettings)
        {
            return new OscReceiverAdapterBinding
            {
                Slug = slug,
                Port = port,
                AdvancedSettings = advancedSettings,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = BlendShapeNameA,
                        addressPattern = OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameA,
                    },
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = BlendShapeNameB,
                        addressPattern = OscAddressFormatter.VRChatParameterPrefix + BlendShapeNameB,
                    },
                },
            };
        }

        private OscSenderAdapterBinding CreateSender(
            string slug,
            int port,
            OscSenderRuntimeSettingsSO advancedSettings,
            IReadOnlyList<string> blendShapeNames)
        {
            var binding = new OscSenderAdapterBinding
            {
                Slug = slug,
                AdvancedSettings = advancedSettings,
                BlendShapeNames = new List<string>(blendShapeNames),
            };
            binding.Configure(Endpoint, port);
            return binding;
        }

        private AdapterBuildContext CreateContext(
            InputSourceRegistry registry,
            FacialOutputBus outputBus,
            GameObject host,
            IReadOnlyList<string> blendShapeNames)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames,
                registry,
                outputBus,
                new UnityTimeProvider(),
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

        private static string BuildLegacySharedJson(int port)
        {
            string portText = port.ToString(CultureInfo.InvariantCulture);
            return "{\"receiverEnabled\":true,\"listenEndpoint\":\"" + Endpoint
                + "\",\"listenPort\":" + portText
                + ",\"bundleMode\":\"individualMessage\""
                + ",\"senderEnabled\":true,\"endpoints\":[{\"endpoint\":\"" + Endpoint
                + "\",\"port\":" + portText
                + ",\"enabled\":true,\"preset\":0}],\"heartbeatIntervalSeconds\":60,\"suppressLoopback\":false}";
        }

        private static int AllocatePort()
        {
            return OscReceiverAdapterBindingTestSupport.AllocatePort();
        }
    }

    /// <summary>
    /// 本ファイル内の 3 fixture で共用するヘルパー。
    /// loopback port はファイル内で 1 本のカウンタから払い出し、fixture 間でも衝突しないようにする。
    /// </summary>
    internal static class OscReceiverAdapterBindingTestSupport
    {
        private const int LoopbackPortBase = 19130;

        private static int s_portCounter;

        /// <summary>
        /// テストごとにユニークな loopback port を払い出して socket 衝突を避ける。
        /// </summary>
        public static int AllocatePort()
        {
            int next = System.Threading.Interlocked.Increment(ref s_portCounter);
            return LoopbackPortBase + next;
        }
    }
}
