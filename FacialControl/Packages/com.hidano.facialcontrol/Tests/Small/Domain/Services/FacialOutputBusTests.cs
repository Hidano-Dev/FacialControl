using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Domain.Services
{
    [TestFixture]
    public class FacialOutputBusTests
    {
        private static readonly Regex InvalidOperationPattern = new Regex("InvalidOperationException");

        [Test]
        public void Publish_NoObservers_ReturnsWithoutError()
        {
            var bus = new FacialOutputBus();

            Assert.IsFalse(bus.HasObservers);
            Assert.DoesNotThrow(() =>
                bus.Publish(Array.Empty<float>(), Array.Empty<GazeSnapshot>()));
        }

        [Test]
        public void Publish_SubscribedObserver_ReceivesBlendShapesAndGazeSnapshots()
        {
            var bus = new FacialOutputBus();
            var observer = new RecordingObserver();
            var blendShapes = new[] { 0.25f, 0.5f, 1.0f };
            var gazeSnapshots = new[]
            {
                new GazeSnapshot("lookLeft", -0.75f, 0.1f),
                new GazeSnapshot("lookRight", 0.8f, -0.2f),
            };

            bus.Subscribe(observer);
            bus.Publish(blendShapes, gazeSnapshots);

            Assert.IsTrue(bus.HasObservers);
            Assert.AreEqual(1, observer.CallCount);
            Assert.That(observer.BlendShapeCalls[0], Is.EqualTo(blendShapes));
            Assert.That(observer.GazeCalls[0], Is.EqualTo(gazeSnapshots));
        }

        [Test]
        public void Unsubscribe_ExistingObserver_RemovesObserver()
        {
            var bus = new FacialOutputBus();
            var observer = new RecordingObserver();

            bus.Subscribe(observer);
            bus.Unsubscribe(observer);
            bus.Publish(new[] { 1.0f }, Array.Empty<GazeSnapshot>());

            Assert.IsFalse(bus.HasObservers);
            Assert.AreEqual(0, observer.CallCount);
        }

        [Test]
        public void Subscribe_DuringPublish_IsAppliedAfterCurrentPublish()
        {
            var bus = new FacialOutputBus();
            var first = new RecordingObserver();
            var second = new RecordingObserver();
            first.OnPublished = () => bus.Subscribe(second);

            bus.Subscribe(first);
            bus.Publish(new[] { 0.1f }, Array.Empty<GazeSnapshot>());
            bus.Publish(new[] { 0.2f }, Array.Empty<GazeSnapshot>());

            Assert.AreEqual(2, first.CallCount);
            Assert.AreEqual(1, second.CallCount);
            Assert.That(second.BlendShapeCalls[0], Is.EqualTo(new[] { 0.2f }));
        }

        [Test]
        public void Unsubscribe_DuringPublish_IsAppliedAfterCurrentPublish()
        {
            var bus = new FacialOutputBus();
            var first = new RecordingObserver();
            var second = new RecordingObserver();
            first.OnPublished = () => bus.Unsubscribe(second);

            bus.Subscribe(first);
            bus.Subscribe(second);
            bus.Publish(new[] { 0.1f }, Array.Empty<GazeSnapshot>());
            bus.Publish(new[] { 0.2f }, Array.Empty<GazeSnapshot>());

            Assert.AreEqual(2, first.CallCount);
            Assert.AreEqual(1, second.CallCount);
            Assert.That(second.BlendShapeCalls[0], Is.EqualTo(new[] { 0.1f }));
        }

        [Test]
        public void Publish_ObserverThrows_LogsExceptionAndContinues()
        {
            var bus = new FacialOutputBus();
            var throwing = new ThrowingObserver();
            var recording = new RecordingObserver();
            var blendShapes = new[] { 0.4f, 0.6f };
            var gazeSnapshots = new[] { new GazeSnapshot("lookUp", 0.0f, 1.0f) };

            bus.Subscribe(throwing);
            bus.Subscribe(recording);

            LogAssert.Expect(LogType.Exception, InvalidOperationPattern);
            Assert.DoesNotThrow(() => bus.Publish(blendShapes, gazeSnapshots));

            Assert.AreEqual(1, throwing.CallCount);
            Assert.AreEqual(1, recording.CallCount);
            Assert.That(recording.BlendShapeCalls[0], Is.EqualTo(blendShapes));
            Assert.That(recording.GazeCalls[0], Is.EqualTo(gazeSnapshots));
        }

        [Test]
        public void Publish_DisconnectedGazeSnapshots_AreExcluded()
        {
            var bus = new FacialOutputBus();
            var observer = new RecordingObserver();
            var connected = new GazeSnapshot("lookUp", 0.0f, 1.0f);
            var gazeSnapshots = new[]
            {
                connected,
                new GazeSnapshot(null, 0.5f, 0.5f),
                new GazeSnapshot(string.Empty, -0.5f, -0.5f),
            };

            bus.Subscribe(observer);
            bus.Publish(Array.Empty<float>(), gazeSnapshots);

            Assert.AreEqual(1, observer.CallCount);
            Assert.That(observer.GazeCalls[0], Is.EqualTo(new[] { connected }));
        }

        [Test]
        public void Subscribe_SameObserverTwice_NotifiesOnlyOnce()
        {
            var bus = new FacialOutputBus();
            var observer = new RecordingObserver();

            bus.Subscribe(observer);
            bus.Subscribe(observer);
            bus.Publish(new[] { 1.0f }, Array.Empty<GazeSnapshot>());

            Assert.AreEqual(1, observer.CallCount);
        }

        [Test]
        public void Unsubscribe_NullObserver_ThrowsArgumentNullException()
        {
            var bus = new FacialOutputBus();

            Assert.Throws<ArgumentNullException>(() => bus.Unsubscribe(null));
        }

        [Test]
        public void Subscribe_NullObserver_ThrowsArgumentNullException()
        {
            var bus = new FacialOutputBus();

            Assert.Throws<ArgumentNullException>(() => bus.Subscribe(null));
        }

        private sealed class RecordingObserver : IFacialOutputObserver
        {
            public int CallCount { get; private set; }
            public Action OnPublished { get; set; }
            public List<float[]> BlendShapeCalls { get; } = new List<float[]>();
            public List<GazeSnapshot[]> GazeCalls { get; } = new List<GazeSnapshot[]>();

            public void OnFacialOutputPublished(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots)
            {
                CallCount++;
                BlendShapeCalls.Add(Copy(postBlendValues));
                GazeCalls.Add(Copy(gazeSnapshots));
                OnPublished?.Invoke();
            }
        }

        private sealed class ThrowingObserver : IFacialOutputObserver
        {
            public int CallCount { get; private set; }

            public void OnFacialOutputPublished(
                ReadOnlySpan<float> postBlendValues,
                ReadOnlySpan<GazeSnapshot> gazeSnapshots)
            {
                CallCount++;
                throw new InvalidOperationException("FacialOutputBusTests observer failure");
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

        private static GazeSnapshot[] Copy(ReadOnlySpan<GazeSnapshot> span)
        {
            var values = new GazeSnapshot[span.Length];
            for (int i = 0; i < span.Length; i++)
            {
                values[i] = span[i];
            }

            return values;
        }
    }
}
