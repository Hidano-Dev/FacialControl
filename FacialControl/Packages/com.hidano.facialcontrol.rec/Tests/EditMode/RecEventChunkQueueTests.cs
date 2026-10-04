using System;
using System.Threading;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecEventChunkQueueTests : SizedTestFixture
    {
        [Test]
        public void TryDequeue_ReturnsEventsInFifoOrderAcrossSegments()
        {
            var queue = new RecEventChunkQueue(segmentCapacity: 2, initialSegments: 2, axisFloatCapacityPerSegment: 4);

            RecEvent first = RecEvent.CreateTriggerOn(0.1d, 0, 0);
            RecEvent second = RecEvent.CreateAnalogSample(0.2d, 1, 2);
            RecEvent third = RecEvent.CreateTriggerOff(0.3d, 0, 0);

            queue.Enqueue(in first, ReadOnlySpan<float>.Empty);
            queue.Enqueue(in second, stackalloc float[] { 0.25f, -0.5f });
            queue.Enqueue(in third, ReadOnlySpan<float>.Empty);

            Assert.That(queue.TryDequeue(out RecEvent dequeuedFirst, out ReadOnlySpan<float> firstAxes, out string firstIdValue), Is.True);
            Assert.That(dequeuedFirst, Is.EqualTo(first));
            Assert.That(firstAxes.Length, Is.Zero);
            Assert.That(firstIdValue, Is.Null);

            Assert.That(queue.TryDequeue(out RecEvent dequeuedSecond, out ReadOnlySpan<float> secondAxes, out string secondIdValue), Is.True);
            Assert.That(dequeuedSecond, Is.EqualTo(second));
            Assert.That(secondAxes.ToArray(), Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(secondIdValue, Is.Null);

            Assert.That(queue.TryDequeue(out RecEvent dequeuedThird, out ReadOnlySpan<float> thirdAxes, out string thirdIdValue), Is.True);
            Assert.That(dequeuedThird, Is.EqualTo(third));
            Assert.That(thirdAxes.Length, Is.Zero);
            Assert.That(thirdIdValue, Is.Null);
            Assert.That(queue.IsEmpty, Is.True);
        }

        [Test]
        public void Enqueue_WhenQueueSaturates_GrowsWithoutDroppingEvents()
        {
            var queue = new RecEventChunkQueue(segmentCapacity: 1, initialSegments: 1, axisFloatCapacityPerSegment: 2);
            RecEvent[] expected =
            {
                RecEvent.CreateTriggerOn(0.1d, 0, 0),
                RecEvent.CreateTriggerOff(0.2d, 0, 0),
                RecEvent.CreateTriggerOn(0.3d, 0, 1),
                RecEvent.CreateTriggerOff(0.4d, 0, 1),
            };

            foreach (ref readonly RecEvent evt in expected.AsSpan())
            {
                queue.Enqueue(in evt, ReadOnlySpan<float>.Empty);
            }

            Assert.That(queue.GrowthCount, Is.EqualTo(3));

            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(queue.TryDequeue(out RecEvent actual, out ReadOnlySpan<float> axes, out string idValue), Is.True);
                Assert.That(actual, Is.EqualTo(expected[i]));
                Assert.That(axes.Length, Is.Zero);
                Assert.That(idValue, Is.Null);
            }

            Assert.That(queue.IsEmpty, Is.True);
        }

        [Test]
        public void ProducerConsumerThreads_PreserveAllEvents()
        {
            const int eventCount = 512;
            var queue = new RecEventChunkQueue(segmentCapacity: 8, initialSegments: 2, axisFloatCapacityPerSegment: 16);
            var consumedEvents = new RecEvent[eventCount];
            var consumedAxes = new float[eventCount][];
            var producerStarted = new ManualResetEventSlim(false);
            Exception producerException = null;
            Exception consumerException = null;

            Thread producer = new Thread(() =>
            {
                try
                {
                    producerStarted.Set();
                    for (int i = 0; i < eventCount; i++)
                    {
                        if ((i & 1) == 0)
                        {
                            RecEvent evt = RecEvent.CreateTriggerOn(i * 0.01d, 0, (ushort)(i % 7));
                            queue.Enqueue(in evt, ReadOnlySpan<float>.Empty);
                        }
                        else
                        {
                            RecEvent evt = RecEvent.CreateAnalogSample(i * 0.01d, 1, 2);
                            Span<float> axes = stackalloc float[2];
                            axes[0] = i;
                            axes[1] = -i;
                            queue.Enqueue(in evt, axes);
                        }
                    }
                }
                catch (Exception ex)
                {
                    producerException = ex;
                }
            });

            Thread consumer = new Thread(() =>
            {
                try
                {
                    producerStarted.Wait();

                    int index = 0;
                    while (index < eventCount)
                    {
                        if (!queue.TryDequeue(out RecEvent evt, out ReadOnlySpan<float> axes, out _))
                        {
                            Thread.Yield();
                            continue;
                        }

                        consumedEvents[index] = evt;
                        consumedAxes[index] = axes.ToArray();
                        index++;
                    }
                }
                catch (Exception ex)
                {
                    consumerException = ex;
                }
            });

            producer.Start();
            consumer.Start();
            producer.Join();
            consumer.Join();

            Assert.That(producerException, Is.Null);
            Assert.That(consumerException, Is.Null);
            Assert.That(queue.IsEmpty, Is.True);

            for (int i = 0; i < eventCount; i++)
            {
                if ((i & 1) == 0)
                {
                    Assert.That(consumedEvents[i], Is.EqualTo(RecEvent.CreateTriggerOn(i * 0.01d, 0, (ushort)(i % 7))));
                    Assert.That(consumedAxes[i], Is.Empty);
                }
                else
                {
                    Assert.That(consumedEvents[i], Is.EqualTo(RecEvent.CreateAnalogSample(i * 0.01d, 1, 2)));
                    Assert.That(consumedAxes[i], Is.EqualTo(new[] { (float)i, (float)-i }));
                }
            }
        }

        [Test]
        public void TryDequeue_ReturnsFloatAndBytePayloadsInFifoOrder()
        {
            var queue = new RecEventChunkQueue(segmentCapacity: 2, initialSegments: 1,
                floatCapacityPerSegment: 4, byteCapacityPerSegment: 4);
            RecEvent first = RecEvent.CreateValueProviderSample(0.1d, 0,
                RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues,
                valueCount: 2, maskByteCount: 2);
            RecEvent second = RecEvent.CreateValueProviderSample(0.2d, 1,
                RecValueProviderFlags.HasMask, valueCount: 0, maskByteCount: 1);

            queue.Enqueue(in first, stackalloc float[] { 0.25f, -0.5f }, stackalloc byte[] { 0x05, 0x80 });
            queue.Enqueue(in second, ReadOnlySpan<float>.Empty, stackalloc byte[] { 0x02 });

            Assert.That(queue.TryDequeue(out RecEvent actualFirst, out ReadOnlySpan<float> firstFloats,
                out ReadOnlySpan<byte> firstBytes, out _), Is.True);
            Assert.That(actualFirst, Is.EqualTo(first));
            Assert.That(firstFloats.ToArray(), Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(firstBytes.ToArray(), Is.EqualTo(new byte[] { 0x05, 0x80 }));

            Assert.That(queue.TryDequeue(out RecEvent actualSecond, out ReadOnlySpan<float> secondFloats,
                out ReadOnlySpan<byte> secondBytes, out _), Is.True);
            Assert.That(actualSecond, Is.EqualTo(second));
            Assert.That(secondFloats.Length, Is.Zero);
            Assert.That(secondBytes.ToArray(), Is.EqualTo(new byte[] { 0x02 }));
        }

        [Test]
        public void Enqueue_WhenSingleRecordExceedsSegmentPayloadCapacity_UsesDedicatedSegment()
        {
            var queue = new RecEventChunkQueue(segmentCapacity: 4, initialSegments: 1,
                floatCapacityPerSegment: 2, byteCapacityPerSegment: 1);
            RecEvent evt = RecEvent.CreateValueProviderSample(0.1d, 0,
                RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues,
                valueCount: 3, maskByteCount: 2);

            queue.Enqueue(in evt, stackalloc float[] { 1f, 2f, 3f }, stackalloc byte[] { 0x01, 0x04 });
            Assert.That(queue.GrowthCount, Is.EqualTo(1));
            Assert.That(queue.TryDequeue(out RecEvent actual, out ReadOnlySpan<float> floats,
                out ReadOnlySpan<byte> bytes, out _), Is.True);
            Assert.That(actual, Is.EqualTo(evt));
            Assert.That(floats.ToArray(), Is.EqualTo(new[] { 1f, 2f, 3f }));
            Assert.That(bytes.ToArray(), Is.EqualTo(new byte[] { 0x01, 0x04 }));
        }

        [Test]
        public void Enqueue_WhenPayloadsFitConfiguredCapacity_DoesNotGrow()
        {
            var queue = new RecEventChunkQueue(segmentCapacity: 2, initialSegments: 1,
                floatCapacityPerSegment: 4, byteCapacityPerSegment: 4);
            RecEvent evt = RecEvent.CreateValueProviderSample(0.1d, 0,
                RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues,
                valueCount: 2, maskByteCount: 2);

            queue.Enqueue(in evt, stackalloc float[] { 1f, 2f }, stackalloc byte[] { 0x01, 0x02 });

            Assert.That(queue.GrowthCount, Is.Zero);
        }
    }
}
