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

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    [SmallTest]
    public class AnalogObservationSamplerTests : SizedTestFixture
    {
        [Test]
        public void Sample_NoObservers_DoesNotReadSources()
        {
            var registry = new InputSourceRegistry();
            var source = new FakeAnalogSource("osc:gaze", 0.1f, 0.2f);
            registry.Register(AdapterSlug.Parse("osc"), "gaze", source);

            var sampler = new AnalogObservationSampler(registry, new FacialInputObservationBus());

            sampler.Sample();

            Assert.That(source.TryReadAxesCount, Is.EqualTo(0));
            Assert.That(source.TryReadVector2Count, Is.EqualTo(0));
            Assert.That(source.TryReadScalarCount, Is.EqualTo(0));
        }

        [Test]
        public void Sample_FirstValidFrame_PublishesAllChangedAnalogSources()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), "gaze", new FakeAnalogSource("osc:gaze", -0.5f, 0.25f));
            registry.Register(AdapterSlug.Parse("arkit"), "brow", new FakeAnalogSource("arkit:brow", 0.75f));

            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new AnalogObservationSampler(registry, bus);

            sampler.Sample();

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[0].sourceId, Is.EqualTo("osc:gaze"));
            Assert.That(observer.Calls[0].axes, Is.EqualTo(new[] { -0.5f, 0.25f }));
            Assert.That(observer.Calls[1].sourceId, Is.EqualTo("arkit:brow"));
            Assert.That(observer.Calls[1].axes, Is.EqualTo(new[] { 0.75f }));
        }

        [Test]
        public void Sample_UnchangedFrame_DoesNotRepublish()
        {
            var registry = new InputSourceRegistry();
            var source = new FakeAnalogSource("osc:gaze", 0.4f, -0.2f);
            registry.Register(AdapterSlug.Parse("osc"), "gaze", source);

            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new AnalogObservationSampler(registry, bus);

            sampler.Sample();
            sampler.Sample();

            Assert.That(observer.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public void Sample_ValueChanged_PublishesUpdatedAxes()
        {
            var registry = new InputSourceRegistry();
            var source = new FakeAnalogSource("osc:gaze", 0.4f, -0.2f);
            registry.Register(AdapterSlug.Parse("osc"), "gaze", source);

            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new AnalogObservationSampler(registry, bus);

            sampler.Sample();
            source.SetValues(0.7f, -0.1f);
            sampler.Sample();

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[1].axes, Is.EqualTo(new[] { 0.7f, -0.1f }));
        }

        [Test]
        public void Sample_InvalidFrame_IsSkippedAndLastValidValueIsRetained()
        {
            var registry = new InputSourceRegistry();
            var source = new FakeAnalogSource("osc:gaze", 0.4f, -0.2f);
            registry.Register(AdapterSlug.Parse("osc"), "gaze", source);

            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new AnalogObservationSampler(registry, bus);

            sampler.Sample();
            source.IsValidValue = false;
            sampler.Sample();
            source.IsValidValue = true;
            sampler.Sample();
            source.SetValues(0.5f, -0.2f);
            sampler.Sample();

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[1].axes, Is.EqualTo(new[] { 0.5f, -0.2f }));
        }

        [Test]
        public void Sample_ReplacedSourceWithDifferentAxisCount_PublishesNewShapeOnNextFrame()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("osc"), "analog", new FakeAnalogSource("osc:analog", 0.25f));

            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new AnalogObservationSampler(registry, bus);

            sampler.Sample();
            registry.Replace(
                AdapterSlug.Parse("osc"),
                "analog",
                new FakeAnalogSource("osc:analog", -0.25f, 0.75f));
            sampler.Sample();

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[0].axes, Is.EqualTo(new[] { 0.25f }));
            Assert.That(observer.Calls[1].axes, Is.EqualTo(new[] { -0.25f, 0.75f }));
        }

        [Test]
        public void Sample_UnregisteredThenReRegisteredSource_PublishesInitialValueAgain()
        {
            var registry = new InputSourceRegistry();
            var slug = AdapterSlug.Parse("osc");
            registry.Register(slug, "gaze", new FakeAnalogSource("osc:gaze", 0.1f, 0.2f));

            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new AnalogObservationSampler(registry, bus);

            sampler.Sample();
            registry.Unregister(slug, "gaze");
            sampler.Sample();
            registry.Register(slug, "gaze", new FakeAnalogSource("osc:gaze", 0.1f, 0.2f));
            sampler.Sample();

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[1].axes, Is.EqualTo(new[] { 0.1f, 0.2f }));
        }

        private sealed class RecordingObserver : IFacialInputObserver
        {
            public List<(string sourceId, float[] axes)> Calls { get; } = new List<(string sourceId, float[] axes)>();

            public void OnTriggerOn(string sourceId, string expressionId)
            {
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
            }

            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                Calls.Add((sourceId, Copy(axes)));
            }

            public void OnValueProviderSample(string sourceId, in ValueProviderSample sample)
            {
            }

            public void OnExpressionActivated(string sourceId, string expressionId)
            {
            }

            public void OnExpressionDeactivated(string sourceId, string expressionId)
            {
            }

            public void OnLayerWeightSample(string layerName, float weight) { }

            public void OnInputSourceWeightSample(string layerName, string slotId, float weight) { }
        }

        private sealed class FakeAnalogSource : IInputSource, IAnalogInputSource
        {
            private float[] _values;

            public FakeAnalogSource(string id, params float[] values)
            {
                Id = id;
                _values = values ?? Array.Empty<float>();
                Type = InputSourceType.ValueProvider;
                BlendShapeCount = 0;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(BlendShapeCount);
                IsValidValue = true;
            }

            public string Id { get; }
            public InputSourceType Type { get; }
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }
            public bool IsValidValue { get; set; }
            public int AxisCount => _values.Length;
            public bool IsValid => IsValidValue;
            public int TryReadScalarCount { get; private set; }
            public int TryReadVector2Count { get; private set; }
            public int TryReadAxesCount { get; private set; }

            public void SetValues(params float[] values)
            {
                _values = values ?? Array.Empty<float>();
            }

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                return false;
            }

            public bool TryReadScalar(out float value)
            {
                TryReadScalarCount++;
                value = default;
                if (!IsValid || AxisCount < 1)
                {
                    return false;
                }

                value = _values[0];
                return true;
            }

            public bool TryReadVector2(out float x, out float y)
            {
                TryReadVector2Count++;
                x = default;
                y = default;
                if (!IsValid || AxisCount < 2)
                {
                    return false;
                }

                x = _values[0];
                y = _values[1];
                return true;
            }

            public bool TryReadAxes(Span<float> output)
            {
                TryReadAxesCount++;
                if (!IsValid || AxisCount == 0)
                {
                    return false;
                }

                int copyLength = output.Length < AxisCount ? output.Length : AxisCount;
                for (int i = 0; i < copyLength; i++)
                {
                    output[i] = _values[i];
                }

                return true;
            }
        }

        private static float[] Copy(ReadOnlySpan<float> axes)
        {
            var values = new float[axes.Length];
            for (int i = 0; i < axes.Length; i++)
            {
                values[i] = axes[i];
            }

            return values;
        }
    }
}
