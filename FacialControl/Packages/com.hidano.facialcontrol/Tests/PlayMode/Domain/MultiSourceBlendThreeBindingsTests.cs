using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
using AdapterInputSourceRegistry = Hidano.FacialControl.Adapters.InputSources.InputSourceRegistry;
using DomainLayerInputSourceRegistry = Hidano.FacialControl.Domain.Services.LayerInputSourceRegistry;

namespace Hidano.FacialControl.Tests.PlayMode.Domain
{
    /// <summary>
    //: verifies that three adapter bindings resolved by slug feed the
    /// unchanged MultiSourceBlend domain pipeline.
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class MultiSourceBlendThreeBindingsTests : SizedTestFixture
    {
        private GameObject _hostGameObject;

        [TearDown]
        public void TearDown()
        {
            if (_hostGameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_hostGameObject);
                _hostGameObject = null;
            }
        }

        [Test]
        public void Aggregate_ThreeAdapterBindingsResolvedBySlug_OutputsWeightedClamp()
        {
            string[] blendShapeNames = { "Blink", "Smile", "A", "I" };
            var profile = CreateProfile();
            var adapterRegistry = new AdapterInputSourceRegistry();
            _hostGameObject = new GameObject("MultiSourceBlendThreeBindingsTestsHost");

            var context = new AdapterBuildContext(
                profile,
                blendShapeNames,
                adapterRegistry,
                new FacialOutputBus(),
                new ManualTimeProvider(),
                _hostGameObject,
                new FakeLipSyncProvider(new[] { 0.4f, 0.8f, 1.0f, 0.6f }));

            var bindings = new AdapterBindingBase[]
            {
                new MockTriggerAdapterBinding(new[] { 0.8f, 0.1f, 0.0f, 1.0f }) { Slug = "mock-trigger" },
                new MockAnalogAdapterBinding(new[] { 0.2f, 0.4f, 0.6f, 0.8f }) { Slug = "mock-analog" },
                new MockLipSyncAdapterBinding { Slug = "lip-sync" },
            };

            for (int i = 0; i < bindings.Length; i++)
            {
                bindings[i].OnStart(in context);
            }

            CollectionAssert.AreEqual(
                new[] { "mock-trigger", "mock-analog", "lip-sync" },
                adapterRegistry.RegisteredIds,
                "Bindings must register sources by their slug keys, not by concrete source ids.");

            var sourceBindings = ResolveLayerBindingsFromProfile(profile, adapterRegistry);
            using var layerRegistry = new DomainLayerInputSourceRegistry(
                profile,
                blendShapeNames.Length,
                sourceBindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                layerRegistry.LayerCount,
                layerRegistry.MaxSourcesPerLayer);

            var declarations = profile.LayerInputSources.Span[0];
            for (int sourceIdx = 0; sourceIdx < declarations.Length; sourceIdx++)
            {
                weightBuffer.SetWeight(0, sourceIdx, declarations[sourceIdx].Weight);
            }

            var aggregator = new LayerInputSourceAggregator(
                layerRegistry,
                weightBuffer,
                blendShapeNames.Length);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            aggregator.Aggregate(deltaTime: 0.016f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            Assert.That(values.Length, Is.EqualTo(blendShapeNames.Length));
            Assert.That(values[0], Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(values[1], Is.EqualTo(0.75f).Within(1e-6f));
            Assert.That(values[2], Is.EqualTo(0.9f).Within(1e-6f));
            Assert.That(values[3], Is.EqualTo(1.0f).Within(1e-6f),
                "0.5*1.0 + 0.25*0.8 + 0.75*0.6 = 1.15 must be clamped to 1.0.");

            Assert.That(sourceBindings[0].source.Type, Is.EqualTo(InputSourceType.ExpressionTrigger));
            Assert.That(sourceBindings[1].source.Type, Is.EqualTo(InputSourceType.ValueProvider));
            Assert.That(sourceBindings[2].source.Type, Is.EqualTo(InputSourceType.ValueProvider));
        }

        private static FacialProfile CreateProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("face", priority: 0, ExclusionMode.Blend)
            };
            var layerInputSources = new[]
            {
                new[]
                {
                    new InputSourceDeclaration("mock-trigger", 0.5f, null),
                    new InputSourceDeclaration("mock-analog", 0.25f, null),
                    new InputSourceDeclaration("lip-sync", 0.75f, null),
                }
            };
            return new FacialProfile(
                "2.0",
                layers: layers,
                expressions: null,
                rendererPaths: null,
                layerInputSources: layerInputSources);
        }

        private static List<(int layerIdx, int sourceIdx, IInputSource source)> ResolveLayerBindingsFromProfile(
            FacialProfile profile,
            AdapterInputSourceRegistry adapterRegistry)
        {
            var sourceBindings = new List<(int layerIdx, int sourceIdx, IInputSource source)>();
            var layers = profile.LayerInputSources.Span;
            for (int layerIdx = 0; layerIdx < layers.Length; layerIdx++)
            {
                var declarations = layers[layerIdx];
                for (int sourceIdx = 0; sourceIdx < declarations.Length; sourceIdx++)
                {
                    if (adapterRegistry.TryResolve(declarations[sourceIdx].Id, out var source))
                    {
                        sourceBindings.Add((layerIdx, sourceIdx, source));
                    }
                }
            }
            return sourceBindings;
        }

        [Serializable]
        private sealed class MockTriggerAdapterBinding : FixedValuesAdapterBindingBase
        {
            public MockTriggerAdapterBinding(float[] values)
                : base(values, InputSourceType.ExpressionTrigger)
            {
            }
        }

        [Serializable]
        private sealed class MockAnalogAdapterBinding : FixedValuesAdapterBindingBase
        {
            public MockAnalogAdapterBinding(float[] values)
                : base(values, InputSourceType.ValueProvider)
            {
            }
        }

        [Serializable]
        private abstract class FixedValuesAdapterBindingBase : AdapterBindingBase
        {
            private readonly float[] _values;
            private readonly InputSourceType _sourceType;

            protected FixedValuesAdapterBindingBase(float[] values, InputSourceType sourceType)
            {
                _values = values;
                _sourceType = sourceType;
            }

            public override void OnStart(in AdapterBuildContext ctx)
            {
                var slug = AdapterSlug.Parse(Slug);
                ctx.InputSourceRegistry.Register(
                    slug,
                    new FixedValuesInputSource(Slug, _sourceType, _values));
            }
        }

        [Serializable]
        private sealed class MockLipSyncAdapterBinding : AdapterBindingBase
        {
            public override void OnStart(in AdapterBuildContext ctx)
            {
                var slug = AdapterSlug.Parse(Slug);
                ctx.InputSourceRegistry.Register(
                    slug,
                    new FixedValuesInputSource(Slug, InputSourceType.ValueProvider, new[] { 0.4f, 0.8f, 1.0f, 0.6f }));
            }
        }

        private sealed class FixedValuesInputSource : IInputSource
        {
            private readonly float[] _values;

            public FixedValuesInputSource(
                string id,
                InputSourceType type,
                float[] values)
            {
                Id = id;
                Type = type;
                BlendShapeCount = values.Length;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(values.Length);
                _values = values;
            }

            public string Id { get; }
            public InputSourceType Type { get; }
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                int length = Math.Min(output.Length, _values.Length);
                for (int i = 0; i < length; i++)
                {
                    output[i] = _values[i];
                }
                return true;
            }
        }

        private sealed class FakeLipSyncProvider : ILipSyncProvider
        {
            private readonly float[] _values;

            public FakeLipSyncProvider(float[] values)
            {
                _values = values;
            }

            public void GetLipSyncValues(Span<float> output)
            {
                int length = Math.Min(output.Length, _values.Length);
                for (int i = 0; i < length; i++)
                {
                    output[i] = _values[i];
                }
            }

            public ReadOnlySpan<string> BlendShapeNames => ReadOnlySpan<string>.Empty;
        }
    }
}
