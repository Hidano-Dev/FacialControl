using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Domain.Services
{
    [TestFixture]
    public class FacialInputObservationBusTests
    {
        private static readonly Regex InvalidOperationPattern = new Regex("InvalidOperationException");

        [Test]
        public void Publish_NoObservers_ReturnsWithoutError()
        {
            var bus = new FacialInputObservationBus();

            Assert.IsFalse(bus.HasObservers);
            Assert.DoesNotThrow(() =>
            {
                bus.OnTriggerOn("input", "smile");
                bus.OnTriggerOff("input", "smile");
                bus.PublishAnalogSample("gaze", new[] { -0.5f, 0.25f });
            });
        }

        [Test]
        public void Publish_SubscribedObserver_ReceivesTriggerAndAnalogEvents()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();
            var axes = new[] { -0.75f, 0.25f };

            bus.Subscribe(observer);
            bus.OnTriggerOn("input", "smile");
            bus.OnTriggerOff("input", "smile");
            bus.PublishAnalogSample("gaze", axes);

            Assert.IsTrue(bus.HasObservers);
            Assert.That(observer.TriggerOnCalls, Is.EqualTo(new[] { ("input", "smile") }));
            Assert.That(observer.TriggerOffCalls, Is.EqualTo(new[] { ("input", "smile") }));
            Assert.That(observer.AnalogCalls[0].sourceId, Is.EqualTo("gaze"));
            Assert.That(observer.AnalogCalls[0].axes, Is.EqualTo(axes));
        }

        [Test]
        public void Unsubscribe_ExistingObserver_RemovesObserver()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();

            bus.Subscribe(observer);
            bus.Unsubscribe(observer);
            bus.OnTriggerOn("input", "smile");
            bus.PublishAnalogSample("gaze", new[] { 1.0f, 0.0f });

            Assert.IsFalse(bus.HasObservers);
            Assert.AreEqual(0, observer.TriggerOnCalls.Count);
            Assert.AreEqual(0, observer.AnalogCalls.Count);
        }

        [Test]
        public void Subscribe_DuringTriggerPublish_IsAppliedAfterCurrentPublish()
        {
            var bus = new FacialInputObservationBus();
            var first = new RecordingObserver();
            var second = new RecordingObserver();
            first.OnTriggerOnAction = () => bus.Subscribe(second);

            bus.Subscribe(first);
            bus.OnTriggerOn("input", "smile");
            bus.OnTriggerOn("input", "angry");

            Assert.That(first.TriggerOnCalls, Is.EqualTo(new[] { ("input", "smile"), ("input", "angry") }));
            Assert.That(second.TriggerOnCalls, Is.EqualTo(new[] { ("input", "angry") }));
        }

        [Test]
        public void Unsubscribe_DuringAnalogPublish_IsAppliedAfterCurrentPublish()
        {
            var bus = new FacialInputObservationBus();
            var first = new RecordingObserver();
            var second = new RecordingObserver();
            first.OnAnalogAction = () => bus.Unsubscribe(second);

            bus.Subscribe(first);
            bus.Subscribe(second);
            bus.PublishAnalogSample("gaze", new[] { 0.1f, 0.2f });
            bus.PublishAnalogSample("gaze", new[] { 0.3f, 0.4f });

            Assert.AreEqual(2, first.AnalogCalls.Count);
            Assert.AreEqual(1, second.AnalogCalls.Count);
            Assert.That(second.AnalogCalls[0].axes, Is.EqualTo(new[] { 0.1f, 0.2f }));
        }

        [Test]
        public void Publish_ObserverThrows_LogsExceptionAndContinues()
        {
            var bus = new FacialInputObservationBus();
            var throwing = new ThrowingObserver();
            var recording = new RecordingObserver();
            var axes = new[] { 0.4f, 0.6f };

            bus.Subscribe(throwing);
            bus.Subscribe(recording);

            LogAssert.Expect(LogType.Exception, InvalidOperationPattern);
            Assert.DoesNotThrow(() => bus.PublishAnalogSample("gaze", axes));

            Assert.AreEqual(1, throwing.AnalogCallCount);
            Assert.AreEqual(1, recording.AnalogCalls.Count);
            Assert.That(recording.AnalogCalls[0].sourceId, Is.EqualTo("gaze"));
            Assert.That(recording.AnalogCalls[0].axes, Is.EqualTo(axes));
        }

        [Test]
        public void Subscribe_SameObserverTwice_NotifiesOnlyOnce()
        {
            var bus = new FacialInputObservationBus();
            var observer = new RecordingObserver();

            bus.Subscribe(observer);
            bus.Subscribe(observer);
            bus.OnTriggerOn("input", "smile");

            Assert.AreEqual(1, observer.TriggerOnCalls.Count);
        }

        [Test]
        public void Unsubscribe_NullObserver_ThrowsArgumentNullException()
        {
            var bus = new FacialInputObservationBus();

            Assert.Throws<ArgumentNullException>(() => bus.Unsubscribe(null));
        }

        [Test]
        public void Subscribe_NullObserver_ThrowsArgumentNullException()
        {
            var bus = new FacialInputObservationBus();

            Assert.Throws<ArgumentNullException>(() => bus.Subscribe(null));
        }

        private sealed class RecordingObserver : IFacialInputObserver
        {
            public Action OnTriggerOnAction { get; set; }
            public Action OnAnalogAction { get; set; }
            public List<(string sourceId, string expressionId)> TriggerOnCalls { get; } =
                new List<(string sourceId, string expressionId)>();
            public List<(string sourceId, string expressionId)> TriggerOffCalls { get; } =
                new List<(string sourceId, string expressionId)>();
            public List<(string sourceId, float[] axes)> AnalogCalls { get; } =
                new List<(string sourceId, float[] axes)>();

            public void OnTriggerOn(string sourceId, string expressionId)
            {
                TriggerOnCalls.Add((sourceId, expressionId));
                OnTriggerOnAction?.Invoke();
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
                TriggerOffCalls.Add((sourceId, expressionId));
            }

            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                AnalogCalls.Add((sourceId, Copy(axes)));
                OnAnalogAction?.Invoke();
            }
        }

        private sealed class ThrowingObserver : IFacialInputObserver
        {
            public int AnalogCallCount { get; private set; }

            public void OnTriggerOn(string sourceId, string expressionId)
            {
            }

            public void OnTriggerOff(string sourceId, string expressionId)
            {
            }

            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes)
            {
                AnalogCallCount++;
                throw new InvalidOperationException("FacialInputObservationBusTests observer failure");
            }
        }

        private static float[] Copy(ReadOnlySpan<float> span)
        {
            var values = new float[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                values[i] = span[i];
            }

            return values;
        }
    }
}
