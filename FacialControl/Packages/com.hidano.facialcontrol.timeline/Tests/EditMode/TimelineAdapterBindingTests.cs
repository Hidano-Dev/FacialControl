using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineAdapterBinding"/>（Slug + 有効フラグの受信許可フラグ）の OnStart / Dispose と legacy フィールドの扱いを検証する。
    /// GameObject と Receiver を生成するため Medium。
    /// </summary>
    [MediumTest]
    public sealed class TimelineAdapterBindingTests : SizedTestFixture
    {
        private const string ExpectedDisplayName = "Timeline";

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        // ================================================================
        // 型
        // ================================================================

        [Test]
        public void Type_HasSerializableAndFacialAdapterBindingAttributes()
        {
            Assert.That(
                typeof(TimelineAdapterBinding).GetCustomAttributes(typeof(SerializableAttribute), inherit: false),
                Has.Length.EqualTo(1));

            object[] attrs = typeof(TimelineAdapterBinding)
                .GetCustomAttributes(typeof(FacialAdapterBindingAttribute), inherit: false);

            Assert.That(attrs, Has.Length.EqualTo(1));
            Assert.That(((FacialAdapterBindingAttribute)attrs[0]).DisplayName, Is.EqualTo(ExpectedDisplayName));
        }

        [Test]
        public void TypeCache_DiscoversTimelineAdapterBinding()
        {
            var discovered = new List<Type>(TypeCache.GetTypesWithAttribute<FacialAdapterBindingAttribute>());

            CollectionAssert.Contains(discovered, typeof(TimelineAdapterBinding));
        }

        [Test]
        public void Type_ImplementsDynamicInputsMarkerAndNotGazeSourceProvider()
        {
            Assert.That(typeof(IAdapterBindingDynamicInputs).IsAssignableFrom(typeof(TimelineAdapterBinding)), Is.True);
            Assert.That(typeof(IGazeSourceProvider).IsAssignableFrom(typeof(TimelineAdapterBinding)), Is.False,
                "乗っ取りは Gaze の提供ではないため Gaze 提供者 interface を実装しない");
        }

        [Test]
        public void NewInstance_IsEnabledWithoutLegacyFields()
        {
            var binding = new TimelineAdapterBinding();

            Assert.That(binding.Enabled, Is.True);
            Assert.That(binding.HasLegacyFields, Is.False);
            Assert.That(binding.Slug, Is.EqualTo("timeline"));
        }

        [Test]
        public void LegacyFields_AreHiddenFromInspectorButStillSerialized()
        {
            foreach (string fieldName in new[] { "targetLayerNames", "channelDefinitions" })
            {
                FieldInfo field = typeof(TimelineAdapterBinding).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(field, Is.Not.Null, fieldName);
                Assert.That(field.GetCustomAttribute<SerializeField>(), Is.Not.Null, fieldName + " はデシリアライズのため残す");
                Assert.That(field.GetCustomAttribute<HideInInspector>(), Is.Not.Null, fieldName + " は Inspector に出さない");
            }

            FieldInfo enabledField = typeof(TimelineAdapterBinding).GetField("enabled", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(enabledField, Is.Not.Null);
            Assert.That(enabledField.GetCustomAttribute<SerializeField>(), Is.Not.Null);
            Assert.That(enabledField.GetCustomAttribute<HideInInspector>(), Is.Null);
        }

        // ================================================================
        // OnStart（有効）
        // ================================================================

        [Test]
        public void OnStart_Enabled_AddsOwnedReceiverAndPassesBindingContext()
        {
            var registry = new FakeInputSourceRegistry();
            var binding = new TimelineAdapterBinding();
            GameObject host = CreateHost();
            AdapterBuildContext context = CreateContext(registry, host);

            try
            {
                binding.OnStart(context);

                Assert.That(binding.Receiver, Is.Not.Null);
                Assert.That(binding.OwnsReceiver, Is.True);
                Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.SameAs(binding.Receiver));
                Assert.That(binding.Receiver.IsBindingAttached, Is.True);
                TimelineBindingContext attached = binding.Receiver.BindingContext;
                Assert.That(attached.Slug.Value, Is.EqualTo("timeline"));
                Assert.That(attached.Registry, Is.SameAs(registry));
                Assert.That(attached.BlendShapeNames, Is.SameAs(context.BlendShapeNames));
                Assert.That(attached.Profile.Layers.Length, Is.EqualTo(2));
                Assert.That(attached.Enabled, Is.True);
                Assert.That(registry.RegisteredIds, Is.Empty,
                    "OnStart は sink を登録しない（接続はセッション開始時に Receiver が行う）");
                Assert.That(binding.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            }
            finally
            {
                binding.Dispose();
            }
        }

        [Test]
        public void OnStart_EnabledWithUserPlacedReceiver_UsesItWithoutOwnership()
        {
            var binding = new TimelineAdapterBinding();
            GameObject host = CreateHost();
            var placed = host.AddComponent<FacialTimelineReceiver>();

            try
            {
                binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));

                Assert.That(binding.Receiver, Is.SameAs(placed));
                Assert.That(binding.OwnsReceiver, Is.False);
                Assert.That(placed.IsBindingAttached, Is.True);
            }
            finally
            {
                binding.Dispose();
            }
        }

        // ================================================================
        // OnStart（無効）
        // ================================================================

        [Test]
        public void OnStart_Disabled_DoesNotCreateReceiver()
        {
            var binding = new TimelineAdapterBinding { Enabled = false };
            GameObject host = CreateHost();

            binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));

            Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.Null, "無効時は Receiver を生成しない");
            Assert.That(binding.Receiver, Is.Null);
            binding.Dispose();
        }

        [Test]
        public void OnStart_DisabledWithUserPlacedReceiver_PassesDisabledContextAndSessionReportsBindingDisabled()
        {
            var binding = new TimelineAdapterBinding { Enabled = false };
            GameObject host = CreateHost();
            var placed = host.AddComponent<FacialTimelineReceiver>();
            bool previousIgnore = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;

            try
            {
                binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));

                Assert.That(placed.IsBindingAttached, Is.True);
                Assert.That(placed.BindingContext.Enabled, Is.False);

                placed.BeginPlaybackSession(null, null);

                Assert.That(placed.SessionState, Is.EqualTo(TimelineSessionState.Failed));
                Assert.That(placed.Diagnostics.Contains(TimelineDiagnosticCode.BindingDisabled), Is.True);
            }
            finally
            {
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = previousIgnore;
                binding.Dispose();
            }
        }

        // ================================================================
        // legacy フィールド
        // ================================================================

        [Test]
        public void OnStart_WithLegacyFields_WarnsOncePerBindingInstance()
        {
            var binding = new TimelineAdapterBinding();
            ResolveField<List<string>>(binding, "targetLayerNames").Add("emotion");
            var other = new TimelineAdapterBinding();
            ResolveField<List<TimelineValueChannelConfig>>(other, "channelDefinitions")
                .Add(new TimelineValueChannelConfig { Sub = "gaze", IsGaze = true });
            GameObject host = CreateHost();
            using var logs = new BindingLogCounter();

            Assert.That(binding.HasLegacyFields, Is.True);
            Assert.That(other.HasLegacyFields, Is.True);

            binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));
            binding.Dispose();
            binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));
            binding.Dispose();

            Assert.That(logs.Warnings, Is.EqualTo(1), "同じ binding インスタンスでは 1 回だけ警告する");

            other.OnStart(CreateContext(new FakeInputSourceRegistry(), host));
            other.Dispose();

            Assert.That(logs.Warnings, Is.EqualTo(2), "別インスタンスは別に警告する");
            Assert.That(logs.Errors, Is.EqualTo(0), "legacy フィールドがあっても再生は継続する");
        }

        [Test]
        public void OnStart_WithoutLegacyFields_DoesNotWarn()
        {
            var binding = new TimelineAdapterBinding();
            GameObject host = CreateHost();
            using var logs = new BindingLogCounter();

            binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));
            binding.Dispose();

            Assert.That(logs.Warnings + logs.Errors, Is.EqualTo(0));
        }

        // ================================================================
        // Dispose の所有判定
        // ================================================================

        [Test]
        public void Dispose_OwnedReceiver_DetachesAndDestroysIt()
        {
            var binding = new TimelineAdapterBinding();
            GameObject host = CreateHost();
            binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));
            Assert.That(binding.OwnsReceiver, Is.True);

            binding.Dispose();

            Assert.That(binding.Receiver, Is.Null);
            Assert.That(binding.OwnsReceiver, Is.False);
            Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.Null, "自分が追加した Receiver は破棄する");
        }

        [Test]
        public void Dispose_UserPlacedReceiver_DetachesButKeepsComponent()
        {
            var binding = new TimelineAdapterBinding();
            GameObject host = CreateHost();
            var placed = host.AddComponent<FacialTimelineReceiver>();
            binding.OnStart(CreateContext(new FakeInputSourceRegistry(), host));

            binding.Dispose();

            Assert.That(binding.Receiver, Is.Null);
            Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.SameAs(placed), "ユーザー配置の Receiver は残す");
            Assert.That(placed.IsBindingAttached, Is.False, "接続は解放する");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private GameObject CreateHost()
        {
            var host = new GameObject("TimelineAdapterBindingTests");
            _created.Add(host);
            return host;
        }

        private static AdapterBuildContext CreateContext(FakeInputSourceRegistry registry, GameObject host)
        {
            return new AdapterBuildContext(
                CreateProfile(),
                new[] { "Smile", "Blink" },
                registry,
                new FacialOutputBus(),
                new FakeTimeProvider(),
                host,
                lipSyncProvider: null);
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                "1.0.0",
                new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                    new LayerDefinition("eye", 1, ExclusionMode.Blend),
                });
        }

        private static T ResolveField<T>(object instance, string fieldName) where T : class
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null, $"Field '{fieldName}' was not found.");

            T value = field.GetValue(instance) as T;
            Assert.That(value, Is.Not.Null, $"Field '{fieldName}' was null.");
            return value;
        }

        /// <summary>binding が出す Console 出力（<c>[TimelineAdapterBinding]</c> 接頭辞）を数える。</summary>
        private sealed class BindingLogCounter : IDisposable
        {
            private readonly bool _previousIgnore;

            public BindingLogCounter()
            {
                _previousIgnore = UnityEngine.TestTools.LogAssert.ignoreFailingMessages;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
                UnityEngine.Application.logMessageReceived += OnLog;
            }

            public int Errors { get; private set; }
            public int Warnings { get; private set; }

            public void Dispose()
            {
                UnityEngine.Application.logMessageReceived -= OnLog;
                UnityEngine.TestTools.LogAssert.ignoreFailingMessages = _previousIgnore;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (condition == null || !condition.Contains("TimelineAdapterBinding"))
                {
                    return;
                }

                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    Errors++;
                }
                else if (type == LogType.Warning)
                {
                    Warnings++;
                }
            }
        }

        private sealed class FakeInputSourceRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _entries =
                new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly List<string> _registeredIds = new List<string>();

            public IReadOnlyList<string> RegisteredIds => _registeredIds;

            public void Register(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Replace(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Unregister(AdapterSlug slug)
            {
                UnregisterInternal(slug.Value);
            }

            public void Unregister(AdapterSlug slug, string sub)
            {
                UnregisterInternal(Compose(slug, sub));
            }

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                return _entries.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
            }

            private void RegisterInternal(string id, IInputSource source)
            {
                _entries[id] = source;
                if (!_registeredIds.Contains(id))
                {
                    _registeredIds.Add(id);
                }
            }

            private void UnregisterInternal(string id)
            {
                _entries.Remove(id);
                _registeredIds.Remove(id);
            }

            private static string Compose(AdapterSlug slug, string sub)
            {
                return string.IsNullOrEmpty(sub) ? slug.Value : slug.Value + ":" + sub;
            }
        }

        private sealed class FakeTimeProvider : ITimeProvider
        {
            public double UnscaledTimeSeconds => 0d;
        }
    }
}
