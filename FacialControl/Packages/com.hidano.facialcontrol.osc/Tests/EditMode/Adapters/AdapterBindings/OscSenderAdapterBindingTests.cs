using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings
{
    [TestFixture]
    [MediumTest]
    public sealed class OscSenderAdapterBindingTests : SizedTestFixture
    {
        private const int PortBase = 19420;
        private static int s_portCounter;

        [Test]
        public void Type_HasSerializableAndFacialAdapterBindingAttributes()
        {
            Assert.That(Attribute.IsDefined(typeof(OscSenderAdapterBinding), typeof(SerializableAttribute)), Is.True);

            object[] attrs = typeof(OscSenderAdapterBinding)
                .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1));
            Assert.That(((FacialAdapterBindingAttribute)attrs[0]).DisplayName, Is.EqualTo("OSC Sender"));
        }

        [Test]
        public void Type_IsSealedAdapterBindingWithParameterlessConstructor()
        {
            Type type = typeof(OscSenderAdapterBinding);

            Assert.That(type.IsSealed, Is.True);
            Assert.That(typeof(AdapterBindingBase).IsAssignableFrom(type), Is.True);
            Assert.That(type.GetConstructor(Type.EmptyTypes), Is.Not.Null);
        }

        [Test]
        public void Ctor_HeartbeatIntervalSeconds_DefaultsToFiveSeconds()
        {
            var binding = new OscSenderAdapterBinding();

            Assert.That(
                binding.HeartbeatIntervalSeconds,
                Is.EqualTo(OscSenderAdapterBinding.DefaultHeartbeatIntervalSeconds));
        }

        [Test]
        public void Ctor_SuppressLoopback_DefaultsToTrue()
        {
            var binding = new OscSenderAdapterBinding();

            Assert.That(binding.SuppressLoopback, Is.True);
        }

        [Test]
        public void Type_ImplementsGazeChannelConsumer()
        {
            Assert.That(typeof(IGazeChannelConsumer).IsAssignableFrom(typeof(OscSenderAdapterBinding)), Is.True);
        }

        [Test]
        public void Type_Endpoints_IsSerializableEndpointListField()
        {
            FieldInfo field = typeof(OscSenderAdapterBinding).GetField(
                "_endpoints",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(List<OscSenderEndpointConfig>)));
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
        }

        [Test]
        public void Type_AdvancedSettings_IsSerializableSenderSettingsField()
        {
            FieldInfo field = typeof(OscSenderAdapterBinding).GetField(
                "_advancedSettings",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(OscSenderRuntimeSettingsSO)));
            Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null);
        }

        [Test]
        public void Type_HasNoSenderEnabledField()
        {
            // 送信の有効/無効は binding を置いたかどうか（と送信先ごとの enabled）で決める。
            FieldInfo field = typeof(OscSenderAdapterBinding).GetField(
                "_senderEnabled",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Null);
        }

        [Test]
        public void Ctor_Endpoints_DefaultsToSingleLocalhostEndpoint()
        {
            var binding = new OscSenderAdapterBinding();

            Assert.That(binding.Endpoints.Count, Is.EqualTo(1));
            Assert.That(binding.Endpoints[0].endpoint, Is.EqualTo(OscSenderEndpointConfig.DefaultEndpoint));
            Assert.That(binding.Endpoints[0].port, Is.EqualTo(OscConfiguration.DefaultSendPort));
            Assert.That(binding.Endpoints[0].enabled, Is.True);
        }

        [Test]
        public void OnStart_NoAdvancedSettings_StartsWithBindingEndpointsAndDefaults()
        {
            // 上級設定アセットなしでも、binding の送信先だけで送信を開始できる。
            var bus = new RecordingFacialOutputBus();
            int port = AllocatePort();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender-no-advanced" };
            binding.Configure("127.0.0.1", port);
            var host = new GameObject("OscSenderAdapterBindingNoAdvancedTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(1));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(port));
                Assert.That(binding.AdvancedSettings, Is.Null);
                Assert.That(binding.HeartbeatIntervalSeconds,
                    Is.EqualTo(OscSenderRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds));
                Assert.That(binding.SuppressLoopback, Is.True);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_MultipleEndpointsInBinding_ConfiguresHostPerEndpoint()
        {
            var bus = new RecordingFacialOutputBus();
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-multi",
                Endpoints = new[]
                {
                    new OscSenderEndpointConfig("127.0.0.1", firstPort),
                    new OscSenderEndpointConfig("127.0.0.1", secondPort),
                },
            };

            var host = new GameObject("OscSenderAdapterBindingMultiEndpointTests");
            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(firstPort));
                Assert.That(binding.GetHelperSender(1).Port, Is.EqualTo(secondPort));
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_AdvancedSettingsAssigned_AppliesAdvancedValues()
        {
            var bus = new RecordingFacialOutputBus();
            var advanced = ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
            advanced.hideFlags = HideFlags.HideAndDontSave;
            advanced.FromJson("{\"heartbeatIntervalSeconds\":2.5,\"suppressLoopback\":false}");

            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-advanced",
                AdvancedSettings = advanced,
            };
            binding.Configure("127.0.0.1", AllocatePort());

            var host = new GameObject("OscSenderAdapterBindingAdvancedTests");
            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.EffectiveSettings, Is.SameAs(advanced));
                Assert.That(binding.HeartbeatIntervalSeconds, Is.EqualTo(2.5f));
                Assert.That(binding.SuppressLoopback, Is.False);
                Assert.That(binding.LoopbackPolicy, Is.Null,
                    "suppressLoopback=false なら loopback 抑制ポリシーを作らない。");
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(advanced);
            }
        }

        [Test]
        public void OnStart_LegacySettingsSenderDisabled_LogsWarningAndSkipsStart()
        {
            // 未移行の旧設定で送信が無効なら、従来どおり起動しない。
            var bus = new RecordingFacialOutputBus();
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            legacy.hideFlags = HideFlags.HideAndDontSave;
            legacy.FromJson(
                "{\"senderEnabled\":false,\"endpoints\":[{\"endpoint\":\"127.0.0.1\",\"port\":19999,\"enabled\":true,\"preset\":0}]}");

            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-legacy-disabled",
                LegacySettings = legacy,
            };

            var host = new GameObject("OscSenderAdapterBindingLegacyDisabledTests");

            LogAssert.Expect(LogType.Warning, new Regex("senderEnabled=false"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void OnStart_LegacySettingsAssigned_UsesLegacyEndpointsAndWarns()
        {
            // 未移行の旧設定が残っていれば、binding 側の送信先より旧設定の送信先を優先して起動する。
            var bus = new RecordingFacialOutputBus();
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            legacy.hideFlags = HideFlags.HideAndDontSave;
            legacy.FromJson(
                "{\"senderEnabled\":true,\"heartbeatIntervalSeconds\":3.0,\"endpoints\":["
                + "{\"endpoint\":\"127.0.0.1\",\"port\":" + firstPort + ",\"enabled\":true,\"preset\":0},"
                + "{\"endpoint\":\"127.0.0.1\",\"port\":" + secondPort + ",\"enabled\":true,\"preset\":0}"
                + "]}");

            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender-legacy",
                LegacySettings = legacy,
            };

            var host = new GameObject("OscSenderAdapterBindingLegacyAppliedTests");

            LogAssert.Expect(LogType.Warning, new Regex("旧形式の設定"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(firstPort));
                Assert.That(binding.GetHelperSender(1).Port, Is.EqualTo(secondPort));
                Assert.That(binding.HeartbeatIntervalSeconds, Is.EqualTo(3f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void OnStart_HeartbeatIntervalBelowMinimum_ClampsAndWarns()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender",
                HeartbeatIntervalSeconds = 0.1f
            };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingHeartbeatClampTests");

            LogAssert.Expect(
                LogType.Warning,
                new Regex("heartbeatIntervalSeconds 0\\.1.*below 0\\.5.*clamped"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(
                    binding.HeartbeatIntervalSeconds,
                    Is.EqualTo(OscSenderAdapterBinding.MinHeartbeatIntervalSeconds));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_ValidContext_AddsHostSubscribesAndGeneratesIdentity()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSender, Is.Not.Null);
                Assert.That(binding.HelperSender.IsConfigured, Is.True);
                Assert.That(bus.Observer, Is.SameAs(binding));
                Assert.That(binding.Identity.Uuid, Is.Not.EqualTo(Guid.Empty));
                Assert.That(binding.Identity.StartedAtUnixMs, Is.GreaterThanOrEqualTo(0L));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_MultipleEnabledEndpoints_AddsIndependentHosts()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", firstPort),
                new OscSenderEndpointConfig("127.0.0.1", secondPort)
            });
            var host = new GameObject("OscSenderAdapterBindingMultiEndpointTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(firstPort));
                Assert.That(binding.GetHelperSender(1).Port, Is.EqualTo(secondPort));
                Assert.That(binding.GetHelperSender(0), Is.Not.SameAs(binding.GetHelperSender(1)));
                Assert.That(host.GetComponents<uOSC.uOscClient>().Length, Is.EqualTo(2));
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_BlendShapeAndGazeWithMultipleEndpoints_StartsEveryEndpoint()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            int firstPort = AllocatePort();
            int secondPort = AllocatePort();
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", firstPort),
                new OscSenderEndpointConfig("127.0.0.1", secondPort)
            });
            binding.ConfigureGazeChannels(new[] { "eyeLook" });
            var host = new GameObject("OscSenderAdapterBindingGazeEndpointsTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile", "まばたき" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(firstPort));
                Assert.That(binding.GetHelperSender(1).Port, Is.EqualTo(secondPort));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_NoBlendShapeAndNoGaze_DoesNotStartAndWarns()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort())
            });
            var host = new GameObject("OscSenderAdapterBindingEmptyLayoutTests");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[OscSenderAdapterBinding\] No BlendShape or gaze channel to send"));

            try
            {
                binding.OnStart(CreateContext(bus, host, Array.Empty<string>()));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(host.GetComponents<OscSender>().Length, Is.EqualTo(0));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_DuplicateEndpoint_NormalizesToOneHostAndWarnsOnce()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            int port = AllocatePort();
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig(" 127.0.0.1 ", port),
                new OscSenderEndpointConfig("127.0.0.1", port),
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort())
            });
            var host = new GameObject("OscSenderAdapterBindingDuplicateEndpointTests");

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"\[OscSenderAdapterBinding\] Duplicate endpoint '127\.0\.0\.1:" + port + "'"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(2));
                Assert.That(binding.GetHelperSender(0).Port, Is.EqualTo(port));
                Assert.That(bus.Observer, Is.SameAs(binding));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_SuppressLoopbackMatchingReceiverEndpoint_RemainsLiveWithoutSenderHost()
        {
            var bus = new RecordingFacialOutputBus();
            int port = AllocatePort();
            var receiver = new OscReceiverAdapterBinding
            {
                Slug = "osc-receiver",
                Port = port
            };
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.Configure("127.0.0.1", port);
            var host = new GameObject("OscSenderAdapterBindingLoopbackSuppressionTests");

            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"\[OscSenderAdapterBinding\] Endpoint '127\.0\.0\.1:" + port + "'.*suppressed"));
            LogAssert.Expect(
                LogType.Warning,
                new Regex(@"\[OscSenderAdapterBinding\] All endpoints were suppressed"));

            try
            {
                binding.OnStart(CreateContext(
                    bus,
                    host,
                    new[] { "smile" },
                    new AdapterBindingBase[] { receiver, binding }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.SameAs(binding));
                Assert.That(binding.LoopbackPolicy, Is.Not.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_SuppressLoopbackDisabledForMatchingReceiverEndpoint_StartsHost()
        {
            var bus = new RecordingFacialOutputBus();
            int port = AllocatePort();
            var receiver = new OscReceiverAdapterBinding
            {
                Slug = "osc-receiver",
                Port = port
            };
            var binding = new OscSenderAdapterBinding
            {
                Slug = "osc-sender",
                SuppressLoopback = false
            };
            binding.Configure("127.0.0.1", port);
            var host = new GameObject("OscSenderAdapterBindingLoopbackDisabledTests");

            try
            {
                binding.OnStart(CreateContext(
                    bus,
                    host,
                    new[] { "smile" },
                    new AdapterBindingBase[] { receiver, binding }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(1));
                Assert.That(binding.HelperSender.Port, Is.EqualTo(port));
                Assert.That(bus.Observer, Is.SameAs(binding));
                Assert.That(binding.LoopbackPolicy, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_NoEnabledEndpoints_WarnsAndDoesNotSubscribe()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.ConfigureEndpoints(new[]
            {
                new OscSenderEndpointConfig("127.0.0.1", AllocatePort(), enabled: false)
            });
            var host = new GameObject("OscSenderAdapterBindingNoEndpointTests");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[OscSenderAdapterBinding\] No enabled endpoints"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSenderCount, Is.EqualTo(0));
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_InvalidSlug_WarnsAndDoesNotSubscribe()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = string.Empty };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingInvalidSlugTests");

            LogAssert.Expect(LogType.Warning, new Regex(@"\[OscSenderAdapterBinding\] Slug '' is invalid"));

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSender, Is.Null);
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Dispose_AfterStart_UnsubscribesAndResetsState()
        {
            var bus = new RecordingFacialOutputBus();
            var binding = new OscSenderAdapterBinding { Slug = "osc-sender" };
            binding.Configure("127.0.0.1", AllocatePort());
            var host = new GameObject("OscSenderAdapterBindingDisposeTests");

            try
            {
                binding.OnStart(CreateContext(bus, host, new[] { "smile" }));
                Assert.That(bus.Observer, Is.SameAs(binding));

                binding.Dispose();

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(binding.HelperSender, Is.Null);
                Assert.That(bus.Observer, Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static AdapterBuildContext CreateContext(
            IFacialOutputBus bus,
            GameObject host,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyList<AdapterBindingBase> adapterBindings = null)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames,
                new InputSourceRegistry(),
                bus,
                new ManualTimeProvider(),
                host,
                lipSyncProvider: null,
                activeExpressionProvider: null,
                adapterBindings: adapterBindings);
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }

        private sealed class RecordingFacialOutputBus : IFacialOutputBus
        {
            public IFacialOutputObserver Observer { get; private set; }

            public bool HasObservers => Observer != null;

            public void Subscribe(IFacialOutputObserver observer)
            {
                Observer = observer;
            }

            public void Unsubscribe(IFacialOutputObserver observer)
            {
                if (ReferenceEquals(Observer, observer))
                {
                    Observer = null;
                }
            }

            public void Publish(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots)
            {
                Observer?.OnFacialOutputPublished(postBlendValues, gazeSnapshots);
            }
        }
    }
}
