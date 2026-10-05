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
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Adapters.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineAdapterBindingTests : SizedTestFixture
    {
        private const string ExpectedDisplayName = "Timeline";

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
        public void GetGazeSourceDeclarations_ValidGazeChannelsDeclareChannelIdsOnly()
        {
            var binding = new TimelineAdapterBinding();
            MutableChannelDefinitions(binding).Add(new TimelineValueChannelConfig
            {
                Sub = "gaze",
                IsGaze = true,
            });
            MutableChannelDefinitions(binding).Add(new TimelineValueChannelConfig
            {
                Sub = "invalid:gaze",
                IsGaze = true,
            });

            var declarations = new List<GazeSourceDeclaration>(binding.GetGazeSourceDeclarations());

            Assert.That(declarations, Has.Count.EqualTo(1));
            Assert.That(declarations[0].ChannelId, Is.EqualTo("gaze"));
            Assert.That(declarations[0].ProvidesLeftRightPair, Is.False);
        }

        [Test]
        public void OnStart_PassesBindingContextToReceiverWithoutRegisteringSinks()
        {
            var registry = new FakeInputSourceRegistry();
            var binding = new TimelineAdapterBinding();
            var host = new GameObject("TimelineAdapterBindingTests");
            AdapterBuildContext context = CreateContext(registry, host);

            try
            {
                binding.OnStart(context);

                Assert.That(binding.Receiver, Is.Not.Null);
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
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void Dispose_ReleasesAndDestroysReceiver()
        {
            var registry = new FakeInputSourceRegistry();
            var binding = new TimelineAdapterBinding();

            var host = new GameObject("TimelineAdapterBindingDisposeTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.Not.Null);

                binding.Dispose();

                Assert.That(binding.Receiver, Is.Null);
                Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
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
        private static List<TimelineValueChannelConfig> MutableChannelDefinitions(TimelineAdapterBinding binding)
        {
            return ResolveField<List<TimelineValueChannelConfig>>(binding, "channelDefinitions");
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
    }
}
