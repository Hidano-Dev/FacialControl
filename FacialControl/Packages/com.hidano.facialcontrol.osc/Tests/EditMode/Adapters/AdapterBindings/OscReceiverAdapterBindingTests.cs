using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Osc.Tests.EditMode.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// task 9.1 EditMode 観測可能完了条件: <see cref="OscReceiverAdapterBinding"/> が
    /// <c>[Serializable]</c> + <c>[FacialAdapterBinding(displayName: "OSC")]</c> 付きであり、
    /// <c>UnityEditor.TypeCache.GetTypesWithAttribute&lt;FacialAdapterBindingAttribute&gt;()</c>
    /// で discovery 列挙されることを assert する。
    /// </summary>
    /// <remarks>
    /// 本ファイルは Red 段階のテストであり、
    /// <c>Hidano.FacialControl.Adapters.AdapterBindings.OscReceiverAdapterBinding</c> が未実装のため
    /// コンパイル時に CS0246 / CS0234 が発生して Red 状態となる（task 9.2 の Green 化対象）。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class OscReceiverAdapterBindingTests : SizedTestFixture
    {
        private const string ExpectedDisplayName = "OSC Receiver";
        private const int PortBase = 19320;

        private static int s_portCounter;

        [Test]
        public void GetDeclaredInputSourceIds_ValidSlug_ReturnsSlugOnly()
        {
            var binding = new OscReceiverAdapterBinding { Slug = "osc-face" };

            string[] ids = ((IAdapterBindingDeclaredInputs)binding).GetDeclaredInputSourceIds().ToArray();

            CollectionAssert.AreEqual(new[] { "osc-face" }, ids, "ルーティングエディタに受信値の入力源（slug）だけを公開する。");
        }

        [Test]
        public void GetDeclaredInputSourceIds_InvalidSlug_ReturnsNothing()
        {
            var binding = new OscReceiverAdapterBinding { Slug = "Invalid Slug" };

            CollectionAssert.IsEmpty(((IAdapterBindingDeclaredInputs)binding).GetDeclaredInputSourceIds());
        }

        [Test]
        public void ConfiguredTargetLayerInputSourceId_NotStarted_ReturnsSlug()
        {
            var binding = new OscReceiverAdapterBinding { Slug = "osc-face", TargetLayer = "lipsync" };
            var target = (IAdapterBindingTargetLayerInput)binding;

            Assert.AreEqual("osc-face", target.ConfiguredTargetLayerInputSourceId, "起動前の Editor 表示でも補う id を返す。");
            Assert.IsNull(target.TargetLayerInputSourceId, "起動していない binding はランタイムでは宣言を補わない。");
        }

        [Test]
        public void Type_HasSerializableAttribute_ForSerializeReferenceRoundTrip()
        {
            object[] attrs = typeof(OscReceiverAdapterBinding)
                .GetCustomAttributes(typeof(SerializableAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1),
                "OscReceiverAdapterBinding に [Serializable] が付いていないと [SerializeReference] の round-trip が破綻する。");
        }

        [Test]
        public void Type_HasFacialAdapterBindingAttributeWithDisplayNameOSCReceiver()
        {
            object[] attrs = typeof(OscReceiverAdapterBinding)
                .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false);

            Assert.That(attrs.Length, Is.EqualTo(1),
                "OscReceiverAdapterBinding には [FacialAdapterBinding] が 1 件だけ付与されているべき。");

            var attr = (FacialAdapterBindingAttribute)attrs[0];
            Assert.That(attr.DisplayName, Is.EqualTo(ExpectedDisplayName),
                $"[FacialAdapterBinding] の displayName は \"{ExpectedDisplayName}\" であるべき。");
        }

        [Test]
        public void Type_DerivesFromAdapterBindingBase()
        {
            Assert.That(typeof(AdapterBindingBase).IsAssignableFrom(typeof(OscReceiverAdapterBinding)), Is.True,
                "OscReceiverAdapterBinding は AdapterBindingBase の派生でなければならない。");
        }

        [Test]
        public void Type_ImplementsResolvedMessageHandler_ForStructViewDispatch()
        {
            Assert.That(
                typeof(IOscResolvedMessageHandler).IsAssignableFrom(typeof(OscReceiverAdapterBinding)),
                Is.True,
                "binding は resolved struct view handler を実装する必要があります。");
        }

        [Test]
        public void Type_IsConcreteSealedClass()
        {
            Type type = typeof(OscReceiverAdapterBinding);

            Assert.That(type.IsAbstract, Is.False,
                "OscReceiverAdapterBinding は具象（非 abstract）クラスでなければならない。");
            Assert.That(type.IsSealed, Is.True,
                "OscReceiverAdapterBinding は sealed でなければならない（拡張は別 binding で実現）。");
        }

        [Test]
        public void Type_HasParameterlessConstructor_ForActivatorCreateInstance()
        {
            // Inspector の Add ドロップダウンが Activator.CreateInstance 等で具象を生成できる必要がある。
            System.Reflection.ConstructorInfo ctor = typeof(OscReceiverAdapterBinding)
                .GetConstructor(Type.EmptyTypes);

            Assert.That(ctor, Is.Not.Null,
                "Activator.CreateInstance で生成可能な parameterless constructor が必要。");
        }

        [Test]
        public void TypeCache_DiscoversOscAdapterBindingViaFacialAdapterBindingAttribute()
        {
            // 各アダプタ package の binding 具象は TypeCache で discovery 列挙される。
            System.Collections.Generic.List<Type> discovered = TypeCache
                .GetTypesWithAttribute<FacialAdapterBindingAttribute>()
                .ToList();

            CollectionAssert.Contains(discovered, typeof(OscReceiverAdapterBinding),
                "TypeCache discovery で OscReceiverAdapterBinding が列挙されるべき。");
        }

        [Test]
        public void TypeCache_DiscoveredEntry_DisplayNameMatchesOSCReceiver()
        {
            FacialAdapterBindingAttribute attr = TypeCache
                .GetTypesWithAttribute<FacialAdapterBindingAttribute>()
                .Where(t => t == typeof(OscReceiverAdapterBinding))
                .Select(t => (FacialAdapterBindingAttribute)t
                    .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false)[0])
                .FirstOrDefault();

            Assert.That(attr, Is.Not.Null);
            Assert.That(attr.DisplayName, Is.EqualTo(ExpectedDisplayName));
        }

        [Test]
        public void Type_HasNoListenEndpointOrEnabledField()
        {
            // 受信は常に全インターフェースで行い、有効/無効は binding を置いたかどうかで決める。
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.Public
                | System.Reflection.BindingFlags.NonPublic;
            Type type = typeof(OscReceiverAdapterBinding);

            Assert.That(type.GetField("_listenEndpoint", Flags), Is.Null, "受信 IP のフィールドは持たない。");
            Assert.That(type.GetField("_endpoint", Flags), Is.Null, "受信 IP のフィールドは持たない。");
            Assert.That(type.GetField("_receiverEnabled", Flags), Is.Null, "受信の有効フラグは持たない。");
            Assert.That(type.GetField("_port", Flags), Is.Not.Null, "受信ポートは binding 本体に持つ。");
        }

        [Test]
        public void OnStart_NoAdvancedSettings_StartsWithPortAndDefaultValues()
        {
            // 上級設定アセットなしでも、ポートだけで受信を開始できる。
            var registry = new InputSourceRegistry();
            int port = AllocatePort();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-port-only",
                Port = port,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = "smile",
                        addressPattern = "/avatar/parameters/smile",
                    }
                }
            };

            var host = new GameObject("OscAdapterBindingPortOnlyTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperHost, Is.Not.Null);
                Assert.That(binding.HelperHost.Port, Is.EqualTo(port));
                Assert.That(binding.HelperHost.Endpoint, Is.EqualTo(OscReceiverAdapterBinding.ListenAllInterfaces),
                    "受信は常に全インターフェースで行う。");
                Assert.That(binding.AdvancedSettings, Is.Null);
                Assert.That(binding.StalenessSeconds, Is.EqualTo(OscReceiverRuntimeSettingsSO.DefaultStalenessSeconds));
                Assert.That(binding.FailSafeMode, Is.EqualTo(FailSafeMode.RevertToBase));
                Assert.That(binding.ConsistencyCheckWarnLog, Is.True);
                Assert.That(binding.BundleMode, Is.EqualTo(BundleInterpretationMode.AtomicSwap));
                Assert.That(binding.BundleAccumulationTimeoutMs,
                    Is.EqualTo(OscReceiverRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_InvalidPort_LogsWarningAndSkipsStart()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-invalid-port",
                Port = 0,
            };

            var host = new GameObject("OscAdapterBindingInvalidPortTests");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("受信ポート 0 が不正"));
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(host.GetComponent<OscReceiverHost>(), Is.Null);
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
            var registry = new InputSourceRegistry();
            int port = AllocatePort();
            var advanced = ScriptableObject.CreateInstance<OscReceiverRuntimeSettingsSO>();
            advanced.hideFlags = HideFlags.HideAndDontSave;
            advanced.FromJson(
                "{\"stalenessSeconds\":0.5,\"failSafeMode\":\"holdLastValue\",\"consistencyCheckWarnLog\":false,"
                + "\"bundleMode\":\"individualMessage\",\"bundleAccumulationTimeoutMs\":12.0}");

            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-advanced-applied",
                Port = port,
                AdvancedSettings = advanced,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = "smile",
                        addressPattern = "/avatar/parameters/smile",
                    }
                }
            };

            var host = new GameObject("OscAdapterBindingAdvancedAppliedTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperHost.Port, Is.EqualTo(port));
                Assert.That(binding.EffectiveSettings, Is.SameAs(advanced));
                Assert.That(binding.StalenessSeconds, Is.EqualTo(0.5f));
                Assert.That(binding.FailSafeMode, Is.EqualTo(FailSafeMode.HoldLastValue));
                Assert.That(binding.ConsistencyCheckWarnLog, Is.False);
                Assert.That(binding.BundleMode, Is.EqualTo(BundleInterpretationMode.IndividualMessage));
                Assert.That(binding.BundleAccumulationTimeoutMs, Is.EqualTo(12f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(advanced);
            }
        }

        [Test]
        public void OnStart_LegacySettingsReceiverDisabled_LogsWarningAndSkipsStart()
        {
            // 未移行の旧設定で受信が無効なら、従来どおり起動しない。
            var registry = new InputSourceRegistry();
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            legacy.hideFlags = HideFlags.HideAndDontSave;
            legacy.FromJson("{\"receiverEnabled\":false,\"listenPort\":19999}");

            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-legacy-disabled",
                LegacySettings = legacy,
            };

            var host = new GameObject("OscAdapterBindingLegacyDisabledTests");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("receiverEnabled=false"));
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.IsStarted, Is.False);
                Assert.That(host.GetComponent<OscReceiverHost>(), Is.Null);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void OnStart_LegacySettingsAssigned_UsesLegacyValuesAndWarns()
        {
            // 未移行の旧設定が残っていれば、binding 側の値より旧設定の値を優先して起動する。
            var registry = new InputSourceRegistry();
            int legacyPort = AllocatePort();
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            legacy.hideFlags = HideFlags.HideAndDontSave;
            legacy.FromJson(
                "{\"receiverEnabled\":true,\"listenPort\":" + legacyPort
                + ",\"bundleMode\":\"individualMessage\",\"stalenessSeconds\":0.25}");

            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-legacy-applied",
                Port = AllocatePort(),
                LegacySettings = legacy,
            };

            var host = new GameObject("OscAdapterBindingLegacyAppliedTests");
            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("旧形式の設定"));
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperHost.Port, Is.EqualTo(legacyPort));
                Assert.That(binding.BundleMode, Is.EqualTo(BundleInterpretationMode.IndividualMessage));
                Assert.That(binding.StalenessSeconds, Is.EqualTo(0.25f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
                UnityEngine.Object.DestroyImmediate(legacy);
            }
        }

        [Test]
        public void Type_ImplementsGazeProviderAndConsumerContracts()
        {
            Assert.That(typeof(IGazeChannelConsumer).IsAssignableFrom(typeof(OscReceiverAdapterBinding)), Is.True);
            Assert.That(typeof(IGazeSourceProvider).IsAssignableFrom(typeof(OscReceiverAdapterBinding)), Is.True);
        }

        [Test]
        public void GazeSourceDeclarations_IncludeManualEntryAndAdvertisementWildcard()
        {
            var binding = new OscReceiverAdapterBinding
            {
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_ARKit_8BS,
                        expressionId = "eye"
                    }
                }
            };

            List<GazeSourceDeclaration> declarations = binding.GetGazeSourceDeclarations().ToList();

            Assert.That(declarations.Count(d => d.ChannelId == null && d.ProvidesLeftRightPair), Is.EqualTo(1));
            Assert.That(declarations.Count(d => d.ChannelId == "eye" && d.ProvidesLeftRightPair), Is.EqualTo(1));
        }

        [Test]
        public void OnStart_EmptyMappings_RegistersEmptyPrimaryInputSource()
        {
            var registry = new InputSourceRegistry();
            int port = AllocatePort();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc-empty",
                Port = port,
                Mappings = new List<OscMappingEntry>()
            };

            var host = new GameObject("OscAdapterBindingEmptyMappingsTests");
            try
            {
                binding.OnStart(CreateContext(registry, host, blendShapeNames: new[] { "smile", "frown" }));

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.HelperHost, Is.Not.Null);
                Assert.That(binding.HelperHost.IsConfigured, Is.True);
                Assert.That(binding.HelperHost.Port, Is.EqualTo(port));
                Assert.That(binding.Buffer, Is.Not.Null);
                Assert.That(binding.Buffer.Size, Is.EqualTo(0));
                Assert.That(binding.InputSource, Is.Not.Null);
                Assert.That(registry.TryResolve("osc-empty", out IInputSource source), Is.True);
                Assert.That(source, Is.SameAs(binding.InputSource));
                Assert.That(source.ContributeMask.Cast<bool>(), Is.All.False);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_GazeVrchatMapping_RegistersVector2InputSourceUnderExpressionId()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding { Slug = "osc" };
            binding.Port = AllocatePort();
            binding.Mappings = new List<OscMappingEntry>
            {
                new OscMappingEntry
                {
                    mode = OscMappingMode.Gaze_VRChat_XY,
                    expressionId = "gaze",
                    addressPattern = "/avatar/parameters/eye",
                    leftRightIndependent = false,
                }
            };

            var host = new GameObject("OscReceiverAdapterBindingTests");
            try
            {
                AdapterBuildContext ctx = CreateContext(registry, host);

                binding.OnStart(ctx);

                Assert.That(binding.IsStarted, Is.True);
                Assert.That(binding.GazeSources.Count, Is.EqualTo(1));
                Assert.That(registry.TryResolve("osc:gaze", out IInputSource inputSource), Is.True);
                var gazeSource = inputSource as GazeVector2InputSource;
                Assert.That(gazeSource, Is.Not.Null);

                gazeSource.Publish(0.25f, -0.5f);
                var config = new GazeChannel { id = "gaze" };

                bool resolved = GazeChannelResolver.TryResolve(
                    config,
                    registry,
                    out ResolvedGazeInputSources sources);

                Assert.That(resolved, Is.True);
                Assert.That(sources.LeftSource, Is.SameAs(gazeSource));
                Assert.That(sources.RightSource, Is.SameAs(gazeSource));
                Assert.That(sources.LeftSource.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.5f).Within(1e-6f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnFixedTick_GazeVrchatMessages_PublishesVector2InputSource()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage,
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

            var host = new GameObject("OscAdapterBindingGazeVrchatTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/eyeX", 0.3f));
                binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/eyeY", -0.4f));
                binding.OnFixedTick(0.02f);

                Assert.That(registry.TryResolve("osc:eye", out IInputSource inputSource), Is.True);
                Assert.That(inputSource, Is.InstanceOf<GazeVector2InputSource>());
                var gaze = (GazeVector2InputSource)inputSource;
                Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.3f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.4f).Within(1e-6f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnFixedTick_GazeVrchatBundle_WaitsForAtomicTimeout()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
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

            var host = new GameObject("OscAdapterBindingGazeVrchatBundleTests");
            try
            {
                binding.OnStart(CreateContext(registry, host, time));

                binding.HelperHost.Receiver.HandleOscMessage(
                    FloatMessage("/avatar/parameters/eyeX", 0.2f, timestamp: 100UL));
                binding.HelperHost.Receiver.HandleOscMessage(
                    FloatMessage("/avatar/parameters/eyeY", -0.6f, timestamp: 100UL));
                binding.OnFixedTick(0.02f);

                var gaze = ResolveGaze(registry, "osc:eye");
                Assert.That(gaze.TryReadVector2(out _, out _), Is.False);

                time.UnscaledTimeSeconds = 0.006;
                binding.OnFixedTick(0.02f);

                Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.2f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.6f).Within(1e-6f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnFixedTick_GazeArKitMessages_PublishesLeftAndRightVector2Sources()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_ARKit_8BS,
                        expressionId = "eye",
                    }
                }
            };

            var host = new GameObject("OscAdapterBindingGazeArKitTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                SendArKit(binding, PerfectSyncEyeLook.EyeLookInLeft, 0.1f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookOutLeft, 0.7f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookUpLeft, 0.2f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookDownLeft, 0.5f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookInRight, 0.6f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookOutRight, 0.1f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookUpRight, 0.8f);
                SendArKit(binding, PerfectSyncEyeLook.EyeLookDownRight, 0.2f);
                binding.OnFixedTick(0.02f);

                Assert.That(registry.TryResolve("osc:eye.left", out IInputSource left), Is.True);
                Assert.That(registry.TryResolve("osc:eye.right", out IInputSource right), Is.True);
                var leftGaze = (GazeVector2InputSource)left;
                var rightGaze = (GazeVector2InputSource)right;

                Assert.That(leftGaze.TryReadVector2(out float leftX, out float leftY), Is.True);
                Assert.That(rightGaze.TryReadVector2(out float rightX, out float rightY), Is.True);
                Assert.That(leftX, Is.EqualTo(0.6f).Within(1e-6f));
                Assert.That(leftY, Is.EqualTo(-0.3f).Within(1e-6f));
                Assert.That(rightX, Is.EqualTo(-0.5f).Within(1e-6f));
                Assert.That(rightY, Is.EqualTo(0.6f).Within(1e-6f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_GazeLeftRightIndependentMissingSourceIds_SkipsEntry()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "eye",
                        addressPattern = "/avatar/parameters/eye",
                        leftRightIndependent = true,
                    }
                }
            };

            LogAssert.Expect(LogType.Warning, new Regex("sourceIdLeft/sourceIdRight"));
            var host = new GameObject("OscAdapterBindingGazeInvalidTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.GazeSources.Count, Is.EqualTo(0));
                Assert.That(registry.TryResolve("osc:eye.left", out _), Is.False);
                Assert.That(registry.TryResolve("osc:eye.right", out _), Is.False);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnFixedTick_StalenessRevertToBase_PublishesGazeZero()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                StalenessSeconds = 0.5f,
                FailSafeMode = FailSafeMode.RevertToBase,
                BundleMode = BundleInterpretationMode.IndividualMessage,
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

            var host = new GameObject("OscAdapterBindingGazeFailSafeTests");
            try
            {
                binding.OnStart(CreateContext(registry, host, time));
                binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/eyeX", 0.8f));
                binding.HelperHost.Receiver.HandleOscMessage(new uOSC.Message("/avatar/parameters/eyeY", -0.2f));
                binding.OnFixedTick(0.02f);

                var gaze = ResolveGaze(registry, "osc:eye");
                Assert.That(gaze.TryReadVector2(out float x, out float y), Is.True);
                Assert.That(x, Is.EqualTo(0.8f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(-0.2f).Within(1e-6f));

                time.UnscaledTimeSeconds = 1.0;
                binding.OnFixedTick(0.02f);

                Assert.That(gaze.TryReadVector2(out x, out y), Is.True);
                Assert.That(x, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(y, Is.EqualTo(0f).Within(1e-6f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_MappingOrderDiffersFromMeshOrder_InitializesSourceInMeshIndexSpace()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage,
            };
            binding.Configure("127.0.0.1", binding.Port, new[]
            {
                new OscMapping("/avatar/parameters/frown", "frown", "emotion"),
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
            });

            var host = new GameObject("OscAdapterBindingMeshIndexTests");
            try
            {
                binding.OnStart(CreateContext(
                    registry,
                    host,
                    blendShapeNames: new[] { "smile", "blink", "frown" }));

                binding.HelperHost.Receiver.HandleOscMessage(
                    new uOSC.Message("/avatar/parameters/frown", 0.75f));
                binding.HelperHost.Receiver.HandleOscMessage(
                    new uOSC.Message("/avatar/parameters/smile", 0.25f));
                binding.OnFixedTick(0.02f);

                Assert.That(registry.TryResolve("osc", out IInputSource source), Is.True);
                var output = new float[] { -1f, -1f, -1f };
                Assert.That(source.TryWriteValues(output), Is.True);
                Assert.That(output[0], Is.EqualTo(0.25f).Within(1e-6f));
                Assert.That(output[1], Is.EqualTo(-1f).Within(1e-6f));
                Assert.That(output[2], Is.EqualTo(0.75f).Within(1e-6f));
                AssertMask(source.ContributeMask, true, false, true);
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void HandleOscMessage_LegacyHeartbeatPresetAndGazeAdvertisement_DoesNotCreateMappingsOrGazeSources()
        {
            var registry = new InputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.IndividualMessage,
                Mappings = new List<OscMappingEntry>(),
            };

            var host = new GameObject("OscAdapterBindingLegacyControlTests");
            try
            {
                binding.OnStart(CreateContext(registry, host, blendShapeNames: new[] { "smile", "frown" }));
                OscInputSource source = binding.InputSource;

                // 旧送信側の heartbeat・preset・gaze 広告は受け取らず、名前の積集合から mapping を作らない。
                binding.HelperHost.Receiver.HandleOscMessage(
                    new uOSC.Message("/_facialcontrol/preset", "arkit"));
                binding.HelperHost.Receiver.HandleOscMessage(
                    new uOSC.Message("/_facialcontrol/blendshape_names", "smile", "frown"));
                binding.HelperHost.Receiver.HandleOscMessage(
                    new uOSC.Message("/_facialcontrol/gaze", "eye", "VRChat_XY"));
                binding.OnFixedTick(0.02f);

                Assert.That(binding.RuntimeMappings.Count, Is.EqualTo(0));
                Assert.That(binding.InputSource, Is.SameAs(source));
                Assert.That(binding.HasAutoGazeRoutes, Is.False);
                Assert.That(binding.GazeSources.Count, Is.EqualTo(0));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void SenderIdentity_NewerStartupWinsAcrossBundleFrames()
        {
            var registry = new InputSourceRegistry();
            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                StalenessSeconds = 0f,
                BundleMode = BundleInterpretationMode.AtomicSwap,
                BundleAccumulationTimeoutMs = 5f,
            };
            binding.Configure("127.0.0.1", binding.Port, new[]
            {
                new OscMapping("/avatar/parameters/smile", "smile", "emotion"),
            });

            var oldSender = new SenderIdentity(Guid.NewGuid(), 1000L);
            var newSender = new SenderIdentity(Guid.NewGuid(), 2000L);
            var host = new GameObject("OscAdapterBindingZombieTests");
            try
            {
                binding.OnStart(CreateContext(registry, host, time));
                var receiver = binding.HelperHost.Receiver;
                receiver.HandleOscMessage(SenderMessage(oldSender, timestamp: 100UL));
                receiver.HandleOscMessage(FloatMessage("/avatar/parameters/smile", 0.1f, timestamp: 100UL));
                receiver.HandleOscMessage(SenderMessage(newSender, timestamp: 200UL));
                receiver.HandleOscMessage(FloatMessage("/avatar/parameters/smile", 0.9f, timestamp: 200UL));

                time.UnscaledTimeSeconds = 0.01;
                binding.OnFixedTick(0.02f);

                Assert.That(binding.CurrentSenderId.HasValue, Is.True);
                Assert.That(binding.CurrentSenderId.Value, Is.EqualTo(newSender));
                Assert.That(registry.TryResolve("osc", out IInputSource source), Is.True);
                var output = new float[1];
                Assert.That(source.TryWriteValues(output), Is.True);
                Assert.That(output[0], Is.EqualTo(0.9f).Within(1e-6f));
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void OnStart_FakeRegistry_RegisteredTypesAreOnlyCatalogObservedTypes()
        {
            var registry = new FakeInputSourceRegistry();
            var binding = new OscReceiverAdapterBinding
            {
                Slug = "osc",
                Port = AllocatePort(),
                Mappings = new List<OscMappingEntry>
                {
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Normal_BlendShape,
                        expressionId = "smile",
                        addressPattern = "/avatar/parameters/smile",
                    },
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "eye",
                        addressPattern = "/avatar/parameters/eye",
                    },
                },
            };
            var host = new GameObject("OscFakeRegistryTests");

            try
            {
                binding.OnStart(CreateContext(registry, host, blendShapeNames: new[] { "smile" }));

                var allowedTypes = new[] { typeof(OscInputSource), typeof(GazeVector2InputSource) };
                Assert.That(registry.RegisterCallCount, Is.GreaterThan(0));
                Assert.That(registry.ReplaceCallCount, Is.EqualTo(0));
                Assert.That(registry.RegisteredSources, Is.Not.Empty);
                foreach (IInputSource source in registry.RegisteredSources)
                {
                    CollectionAssert.Contains(allowedTypes, source.GetType(),
                        $"登録型 {source.GetType().FullName} は観測対象型ではありません。");
                    Assert.That(source, Is.Not.TypeOf<OscFloatAnalogSource>());
                    Assert.That(source, Is.Not.TypeOf<ArKitOscAnalogSource>());
                }
            }
            finally
            {
                binding.Dispose();
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        private static AdapterBuildContext CreateContext(
            IInputSourceRegistry registry,
            GameObject host,
            ManualTimeProvider timeProvider = null,
            IReadOnlyList<string> blendShapeNames = null)
        {
            return new AdapterBuildContext(
                new FacialProfile("2.0.0"),
                blendShapeNames ?? Array.Empty<string>(),
                registry,
                new FacialOutputBus(),
                timeProvider ?? new ManualTimeProvider(),
                host,
                lipSyncProvider: null);
        }

        private static int AllocatePort()
        {
            return PortBase + System.Threading.Interlocked.Increment(ref s_portCounter);
        }

        private static void SendArKit(OscReceiverAdapterBinding binding, string name, float value)
        {
            binding.HelperHost.Receiver.HandleOscMessage(
                new uOSC.Message(PerfectSyncEyeLook.ArKitAddressPrefix + name, value));
        }

        private static GazeVector2InputSource ResolveGaze(InputSourceRegistry registry, string id)
        {
            Assert.That(registry.TryResolve(id, out IInputSource source), Is.True);
            Assert.That(source, Is.InstanceOf<GazeVector2InputSource>());
            return (GazeVector2InputSource)source;
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

        private static void AssertMask(BitArray mask, params bool[] expected)
        {
            Assert.That(mask.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(mask[i], Is.EqualTo(expected[i]), $"mask[{i}]");
            }
        }
    }
}
