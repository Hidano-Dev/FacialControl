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
        public void OnStart_RegistersStateAnalogAndGazeSinks_AndConfiguresReceiver()
        {
            var registry = new FakeInputSourceRegistry();
            var binding = new TimelineAdapterBinding();
            MutableTargetLayerNames(binding).Add("emotion");
            MutableTargetLayerNames(binding).Add("eye");
            MutableChannelDefinitions(binding).Add(new TimelineValueChannelConfig
            {
                Sub = "analog-main",
                AxisCount = 3,
            });
            MutableChannelDefinitions(binding).Add(new TimelineValueChannelConfig
            {
                Sub = "gaze-main",
                AxisCount = 2,
                IsGaze = true,
                TakeoverSourceId = "live:gaze",
            });

            var host = new GameObject("TimelineAdapterBindingTests");
            try
            {
                binding.OnStart(CreateContext(registry, host));

                Assert.That(binding.Receiver, Is.Not.Null);
                Assert.That(host.GetComponent<FacialTimelineReceiver>(), Is.SameAs(binding.Receiver));

                Assert.That(registry.TryResolve("timeline:emotion", out IInputSource emotionSource), Is.True);
                Assert.That(registry.TryResolve("timeline:eye", out IInputSource eyeSource), Is.True);
                Assert.That(registry.TryResolve("timeline:emotion:state", out IInputSource emotionStateSource), Is.True);
                Assert.That(registry.TryResolve("timeline:eye:state", out IInputSource eyeStateSource), Is.True);
                Assert.That(registry.TryResolve("timeline:analog-main", out IInputSource analogSource), Is.True);
                Assert.That(registry.TryResolve("timeline:gaze-0", out IInputSource gazeSource), Is.True);

                Assert.That(emotionSource, Is.InstanceOf<TimelineBakedValueSink>());
                Assert.That(eyeSource, Is.InstanceOf<TimelineBakedValueSink>());
                Assert.That(emotionStateSource, Is.InstanceOf<TimelineExpressionStateSink>());
                Assert.That(eyeStateSource, Is.InstanceOf<TimelineExpressionStateSink>());
                Assert.That(analogSource, Is.InstanceOf<TimelineAnalogInputSource>());
                Assert.That(gazeSource, Is.InstanceOf<TimelineGazeInputSource>());
                Assert.That(((TimelineAnalogInputSource)analogSource).AxisCount, Is.EqualTo(3));

                Assert.That(binding.Receiver.TryGetExpressionSink("emotion", out var emotionSink), Is.True);
                Assert.That(binding.Receiver.TryGetExpressionSink("eye", out var eyeSink), Is.True);
                Assert.That(binding.Receiver.TryGetExpressionValueSink("emotion", out var emotionValueSink), Is.True);
                Assert.That(binding.Receiver.TryGetExpressionValueSink("eye", out var eyeValueSink), Is.True);
                Assert.That(binding.Receiver.TryGetAnalogSink("analog-main", out var resolvedAnalog), Is.True);
                Assert.That(binding.Receiver.TryGetGazeSink("gaze-main", out var resolvedGaze), Is.True);
                Assert.That(emotionSink, Is.SameAs(emotionStateSource));
                Assert.That(eyeSink, Is.SameAs(eyeStateSource));
                Assert.That(emotionValueSink, Is.SameAs(emotionSource));
                Assert.That(eyeValueSink, Is.SameAs(eyeSource));
                Assert.That(resolvedAnalog, Is.SameAs(analogSource));
                Assert.That(resolvedGaze, Is.SameAs(gazeSource));
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
            MutableTargetLayerNames(binding).Add("emotion");

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
        private static List<string> MutableTargetLayerNames(TimelineAdapterBinding binding)
        {
            return ResolveField<List<string>>(binding, "targetLayerNames");
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
