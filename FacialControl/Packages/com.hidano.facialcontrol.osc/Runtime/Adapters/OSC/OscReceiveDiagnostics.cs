using System.Threading;

namespace Hidano.FacialControl.Adapters.OSC
{
    public enum OscDiagnosticWarning : byte
    {
        DroppedDatagram,
        OversizedDatagram,
        TruncatedDatagram,
        MalformedElement
    }

    public sealed class OscReceiveDiagnostics
    {
        private long _receivedDatagrams;
        private long _appliedDatagrams;
        private long _droppedDatagrams;
        private long _oversizedDatagrams;
        private long _truncatedDatagrams;
        private long _malformedElements;
        private long _staleRecords;
        private long _fixedTicks;
        private int _warningMask;

        public long ReceivedDatagramCount => Interlocked.Read(ref _receivedDatagrams);
        public long AppliedDatagramCount => Interlocked.Read(ref _appliedDatagrams);
        public long DroppedDatagramCount => Interlocked.Read(ref _droppedDatagrams);
        public long OversizedDatagramCount => Interlocked.Read(ref _oversizedDatagrams);
        public long TruncatedDatagramCount => Interlocked.Read(ref _truncatedDatagrams);
        public long MalformedElementCount => Interlocked.Read(ref _malformedElements);
        public long StaleRecordCount => Interlocked.Read(ref _staleRecords);
        public long FixedTickCount => Interlocked.Read(ref _fixedTicks);

        public void IncrementReceivedDatagrams() => Interlocked.Increment(ref _receivedDatagrams);
        public void IncrementAppliedDatagrams() => Interlocked.Increment(ref _appliedDatagrams);
        public void IncrementAppliedDatagramsBy(int count) => Interlocked.Add(ref _appliedDatagrams, count);
        public void IncrementDroppedDatagrams() => Interlocked.Increment(ref _droppedDatagrams);
        public void IncrementOversizedDatagrams() => Interlocked.Increment(ref _oversizedDatagrams);
        public void IncrementTruncatedDatagrams() => Interlocked.Increment(ref _truncatedDatagrams);
        public void IncrementMalformedElements() => Interlocked.Increment(ref _malformedElements);
        public void IncrementStaleRecords() => Interlocked.Increment(ref _staleRecords);
        public void IncrementFixedTicks() => Interlocked.Increment(ref _fixedTicks);

        public bool TryMarkWarning(OscDiagnosticWarning warning)
        {
            int bit = 1 << (int)warning;
            int oldValue;
            do
            {
                oldValue = Volatile.Read(ref _warningMask);
                if ((oldValue & bit) != 0) return false;
            } while (Interlocked.CompareExchange(ref _warningMask, oldValue | bit, oldValue) != oldValue);
            return true;
        }
    }
}
