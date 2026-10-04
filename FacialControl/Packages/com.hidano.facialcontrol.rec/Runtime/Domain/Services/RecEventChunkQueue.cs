using System;
using System.Threading;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Single-producer/single-consumer chunked queue for handing recording events from the main thread to a writer thread.
    /// </summary>
    public sealed class RecEventChunkQueue
    {
        private readonly int _segmentCapacity;
        private readonly int _axisFloatCapacityPerSegment;
        private readonly int _byteCapacityPerSegment;

        private Segment _producerSegment;
        private Segment _consumerSegment;
        private Segment _freeListHead;
        private int _growthCount;

        public RecEventChunkQueue(int segmentCapacity, int initialSegments, int axisFloatCapacityPerSegment)
            : this(segmentCapacity, initialSegments, axisFloatCapacityPerSegment, 0)
        {
        }

        public RecEventChunkQueue(int segmentCapacity, int initialSegments, int floatCapacityPerSegment, int byteCapacityPerSegment)
        {
            if (segmentCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(segmentCapacity), "Segment capacity must be greater than zero.");
            }

            if (initialSegments <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialSegments), "Initial segment count must be greater than zero.");
            }

            if (floatCapacityPerSegment <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(floatCapacityPerSegment), "Float capacity must be greater than zero.");
            }

            if (byteCapacityPerSegment < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCapacityPerSegment), "Byte capacity must not be negative.");
            }

            _segmentCapacity = segmentCapacity;
            _axisFloatCapacityPerSegment = floatCapacityPerSegment;
            _byteCapacityPerSegment = byteCapacityPerSegment;

            _producerSegment = new Segment(segmentCapacity, floatCapacityPerSegment, byteCapacityPerSegment);
            _consumerSegment = _producerSegment;

            for (int i = 1; i < initialSegments; i++)
            {
                PushFreeSegment(new Segment(segmentCapacity, floatCapacityPerSegment, byteCapacityPerSegment));
            }
        }

        public bool IsEmpty
        {
            get
            {
                Segment segment = _consumerSegment;
                if (segment.ReadCount < Volatile.Read(ref segment.PublishedCount))
                {
                    return false;
                }

                return Volatile.Read(ref segment.Next) == null;
            }
        }

        public int GrowthCount => Volatile.Read(ref _growthCount);

        /// <summary>
        /// Producer-thread only. Adds an event to the queue and never drops it; saturation grows the queue by adding a new segment.
        /// </summary>
        public void Enqueue(in RecEvent evt, ReadOnlySpan<float> axes, string idValue = null)
        {
            Enqueue(in evt, axes, ReadOnlySpan<byte>.Empty, idValue);
        }

        /// <summary>
        /// Producer-thread only. Adds an event and both payloads before publishing it with the single SPSC counter.
        /// </summary>
        public void Enqueue(in RecEvent evt, ReadOnlySpan<float> floats, ReadOnlySpan<byte> bytes, string idValue = null)
        {
            ValidatePayloads(evt, floats, bytes);

            Segment segment = _producerSegment;
            if (!segment.CanWrite(floats.Length, bytes.Length))
            {
                segment = MoveProducerToNextSegment(floats.Length, bytes.Length);
            }

            segment.Write(in evt, floats, bytes, idValue);
            Volatile.Write(ref segment.PublishedCount, segment.WriteCount);
        }

        /// <summary>
        /// Consumer-thread only. Returns true when an event is available. The returned axes span is valid until the next TryDequeue call.
        /// </summary>
        public bool TryDequeue(out RecEvent evt, out ReadOnlySpan<float> axes, out string idValue)
        {
            bool result = TryDequeue(out evt, out axes, out _, out idValue);
            return result;
        }

        public bool TryDequeue(out RecEvent evt, out ReadOnlySpan<float> floats, out ReadOnlySpan<byte> bytes, out string idValue)
        {
            Segment segment = _consumerSegment;

            while (true)
            {
                int publishedCount = Volatile.Read(ref segment.PublishedCount);
                if (segment.ReadCount < publishedCount)
                {
                    segment.Read(out evt, out floats, out bytes, out idValue);
                    return true;
                }

                Segment next = Volatile.Read(ref segment.Next);
                if (next == null)
                {
                    evt = default;
                    floats = default;
                    bytes = default;
                    idValue = null;
                    return false;
                }

                MoveConsumerToNextSegment(segment, next);
                segment = next;
            }
        }

        private Segment MoveProducerToNextSegment(int floatCount, int byteCount)
        {
            Segment next = PopFreeSegment();
            if (next != null && !next.CanWrite(floatCount, byteCount))
            {
                PushFreeSegment(next);
                next = null;
            }

            if (next == null)
            {
                bool needsDedicatedSegment = floatCount > _axisFloatCapacityPerSegment
                    || byteCount > _byteCapacityPerSegment;
                next = needsDedicatedSegment
                    ? new Segment(1, floatCount, byteCount)
                    : new Segment(_segmentCapacity, _axisFloatCapacityPerSegment, _byteCapacityPerSegment);
                Interlocked.Increment(ref _growthCount);
            }

            Volatile.Write(ref _producerSegment.Next, next);
            _producerSegment = next;
            return next;
        }

        private void MoveConsumerToNextSegment(Segment current, Segment next)
        {
            _consumerSegment = next;
            current.ResetForReuse();
            PushFreeSegment(current);
        }

        private Segment PopFreeSegment()
        {
            while (true)
            {
                Segment head = Volatile.Read(ref _freeListHead);
                if (head == null)
                {
                    return null;
                }

                Segment next = head.FreeNext;
                if (Interlocked.CompareExchange(ref _freeListHead, next, head) == head)
                {
                    head.FreeNext = null;
                    return head;
                }
            }
        }

        private void PushFreeSegment(Segment segment)
        {
            while (true)
            {
                Segment currentHead = Volatile.Read(ref _freeListHead);
                segment.FreeNext = currentHead;
                if (Interlocked.CompareExchange(ref _freeListHead, segment, currentHead) == currentHead)
                {
                    return;
                }
            }
        }

        private static void ValidatePayloads(in RecEvent evt, ReadOnlySpan<float> floats, ReadOnlySpan<byte> bytes)
        {
            if (floats.Length != evt.PayloadFloatCount)
            {
                throw new ArgumentException("Float payload length must match the event payload count.", nameof(floats));
            }

            int expectedByteCount = (evt.Flags & RecValueProviderFlags.HasMask) != 0 ? evt.MaskByteCount : 0;
            if (bytes.Length != expectedByteCount)
            {
                throw new ArgumentException("Byte payload length must match the event mask byte count.", nameof(bytes));
            }
        }

        private sealed class Segment
        {
            private readonly RecEvent[] _events;
            private readonly int[] _axisStarts;
            private readonly float[] _axisValues;
            private readonly int[] _byteStarts;
            private readonly byte[] _byteValues;
            private readonly string[] _idValues;

            public Segment(int segmentCapacity, int floatCapacityPerSegment, int byteCapacityPerSegment)
            {
                _events = new RecEvent[segmentCapacity];
                _axisStarts = new int[segmentCapacity];
                _axisValues = new float[floatCapacityPerSegment];
                _byteStarts = new int[segmentCapacity];
                _byteValues = new byte[byteCapacityPerSegment];
                _idValues = new string[segmentCapacity];
            }

            public Segment Next;

            public Segment FreeNext;

            public int WriteCount;

            public int ReadCount;

            public int AxisWriteCount;

            public int PublishedCount;

            public bool CanWrite(int floatCount, int byteCount)
            {
                return WriteCount < _events.Length
                    && AxisWriteCount + floatCount <= _axisValues.Length
                    && ByteWriteCount + byteCount <= _byteValues.Length;
            }

            public int ByteWriteCount;

            public void Write(in RecEvent evt, ReadOnlySpan<float> floats, ReadOnlySpan<byte> bytes, string idValue)
            {
                int index = WriteCount;
                int axisStart = AxisWriteCount;
                int byteStart = ByteWriteCount;

                if (!floats.IsEmpty)
                {
                    floats.CopyTo(_axisValues.AsSpan(axisStart, floats.Length));
                }

                if (!bytes.IsEmpty)
                {
                    bytes.CopyTo(_byteValues.AsSpan(byteStart, bytes.Length));
                }

                _axisStarts[index] = axisStart;
                _byteStarts[index] = byteStart;
                _events[index] = evt;
                _idValues[index] = idValue;
                AxisWriteCount += floats.Length;
                ByteWriteCount += bytes.Length;
                WriteCount = index + 1;
            }

            public void Read(out RecEvent evt, out ReadOnlySpan<float> floats, out ReadOnlySpan<byte> bytes, out string idValue)
            {
                int index = ReadCount;
                evt = _events[index];
                idValue = _idValues[index];
                _idValues[index] = null;
                floats = evt.PayloadFloatCount == 0
                    ? ReadOnlySpan<float>.Empty
                    : new ReadOnlySpan<float>(_axisValues, _axisStarts[index], evt.PayloadFloatCount);
                bytes = evt.MaskByteCount == 0
                    ? ReadOnlySpan<byte>.Empty
                    : new ReadOnlySpan<byte>(_byteValues, _byteStarts[index], evt.MaskByteCount);
                ReadCount = index + 1;
            }

            public void ResetForReuse()
            {
                Next = null;
                FreeNext = null;
                WriteCount = 0;
                ReadCount = 0;
                AxisWriteCount = 0;
                ByteWriteCount = 0;
                PublishedCount = 0;
            }
        }
    }
}
