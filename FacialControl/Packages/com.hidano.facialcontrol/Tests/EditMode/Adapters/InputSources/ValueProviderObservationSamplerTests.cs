using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters.InputSources
{
    [TestFixture]
    [SmallTest]
    public sealed class ValueProviderObservationSamplerTests : SizedTestFixture
    {
        [Test]
        public void Sample_ValueBitsUseExactComparison()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new ValueProviderObservationSampler(bus);
            var source = new FakeValueProvider("osc:face", 2);

            sampler.OnSourceValuesObserved(0, 0, source, InputSourceId.Parse("osc:face"), true,
                new float[] { 0f, 1f });
            sampler.OnSourceValuesObserved(0, 0, source, InputSourceId.Parse("osc:face"), true,
                new float[] { -0f, 1f });

            Assert.That(observer.Samples.Count, Is.EqualTo(2));
            Assert.That(observer.Samples[1].ValuesChanged, Is.True);
        }

        [Test]
        public void Sample_MaskChangeForcesValueChange()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new ValueProviderObservationSampler(bus);
            var source = new FakeValueProvider("osc:face", 2);

            sampler.OnSourceValuesObserved(0, 0, source, InputSourceId.Parse("osc:face"), true,
                new float[] { 0.2f, 0.4f });
            source.Mask[1] = false;
            sampler.OnSourceValuesObserved(0, 0, source, InputSourceId.Parse("osc:face"), true,
                new float[] { 0.2f, 0.4f });

            Assert.That(observer.Samples.Count, Is.EqualTo(2));
            Assert.That(observer.Samples[1].MaskChanged, Is.True);
            Assert.That(observer.Samples[1].ValuesChanged, Is.True);
        }

        [Test]
        public void Sample_InvalidFramesAreCollapsedUntilValidityChanges()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new ValueProviderObservationSampler(bus);
            var source = new FakeValueProvider("osc:face", 1);
            var id = InputSourceId.Parse("osc:face");

            sampler.OnSourceValuesObserved(0, 0, source, id, true, new float[] { 0.5f });
            sampler.OnSourceValuesObserved(0, 0, source, id, false, new float[] { 0f });
            sampler.OnSourceValuesObserved(0, 0, source, id, false, new float[] { 0f });
            sampler.OnSourceValuesObserved(0, 0, source, id, true, new float[] { 0.5f });

            Assert.That(observer.Samples.Count, Is.EqualTo(3));
            Assert.That(observer.Samples[1].IsValid, Is.False);
            Assert.That(observer.Samples[2].ValidityChanged, Is.True);
            Assert.That(observer.Samples[2].ValuesChanged, Is.False);
        }

        [Test]
        public void Sample_SameIdDifferentInstanceRepublishesFullState()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new ValueProviderObservationSampler(bus);
            var id = InputSourceId.Parse("osc:face");

            sampler.OnSourceValuesObserved(0, 0, new FakeValueProvider("osc:face", 1), id, true,
                new float[] { 0.5f });
            sampler.OnSourceValuesObserved(0, 0, new FakeValueProvider("osc:face", 1), id, true,
                new float[] { 0.5f });

            Assert.That(observer.Samples.Count, Is.EqualTo(2));
            Assert.That(observer.Samples[1].ValidityChanged, Is.True);
            Assert.That(observer.Samples[1].ValuesChanged, Is.True);
            Assert.That(observer.Samples[1].MaskChanged, Is.True);
        }

        [Test]
        public void Sample_SameSourceBoundToTwoLayersPublishesOnceWhenStateIsSame()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            bus.Subscribe(observer);
            var sampler = new ValueProviderObservationSampler(bus);
            var source = new FakeValueProvider("osc:face", 1);
            var id = InputSourceId.Parse("osc:face");

            sampler.OnSourceValuesObserved(0, 0, source, id, true, new float[] { 0.5f });
            sampler.OnSourceValuesObserved(1, 0, source, id, true, new float[] { 0.5f });

            Assert.That(observer.Samples.Count, Is.EqualTo(1));
        }

        private sealed class RecordingObserver : IFacialInputObserver
        {
            public List<ValueProviderSampleRecord> Samples { get; } = new List<ValueProviderSampleRecord>();

            public void OnTriggerOn(string sourceId, string expressionId) { }
            public void OnTriggerOff(string sourceId, string expressionId) { }
            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes) { }
            public void OnExpressionActivated(string sourceId, string expressionId) { }
            public void OnExpressionDeactivated(string sourceId, string expressionId) { }

            public void OnValueProviderSample(string sourceId, in ValueProviderSample sample)
            {
                Samples.Add(new ValueProviderSampleRecord(
                    sample.IsValid, sample.ValidityChanged, sample.ValuesChanged, sample.MaskChanged));
            }
        }

        private readonly struct ValueProviderSampleRecord
        {
            public ValueProviderSampleRecord(bool isValid, bool validityChanged, bool valuesChanged, bool maskChanged)
            {
                IsValid = isValid;
                ValidityChanged = validityChanged;
                ValuesChanged = valuesChanged;
                MaskChanged = maskChanged;
            }

            public bool IsValid { get; }
            public bool ValidityChanged { get; }
            public bool ValuesChanged { get; }
            public bool MaskChanged { get; }
        }

        private sealed class FakeValueProvider : ValueProviderInputSourceBase
        {
            public FakeValueProvider(string id, int count)
                : base(InputSourceId.Parse(id), count)
            {
                Mask = new BitArray(count, true);
            }

            public BitArray Mask { get; }
            public override BitArray ContributeMask => Mask;
            public override bool TryWriteValues(Span<float> output) => false;
        }
    }
}
