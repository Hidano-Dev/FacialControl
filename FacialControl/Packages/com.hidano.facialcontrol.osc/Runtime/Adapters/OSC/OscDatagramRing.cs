using System;
using System.Net;
using System.Threading;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>固定スロットのロック保護 SPSC データグラムリング。</summary>
    public sealed class OscDatagramRing
    {
        private enum SlotState : byte { Free, Reserved, Committed }

        private struct SlotHeader
        {
            public SlotState State;
            public int Length;
            public int RecordCount;
            public int TableVersion;
            public uint Sequence;
        }

        private readonly object _sync = new object();
        private readonly byte[] _bytes;
        private readonly OscResolvedMessage[] _records;
        private readonly SlotHeader[] _headers;
        // 送信元を取っている間だけ受信スレッドが入れる（取っていない間は null）。
        private readonly EndPoint[] _remotes;
        private readonly int _slotBytes;
        private readonly int _slotCount;
        private readonly int _recordsPerSlot;
        private readonly OscReceiveDiagnostics _diagnostics;
        private long _head;
        private long _committedTail;
        private long _reservedTail;
        private uint _sequence;

        public OscDatagramRing(in OscReceiveOptions options, OscReceiveDiagnostics diagnostics)
        {
            _diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
            _slotBytes = options.DatagramSlotBytes;
            _slotCount = options.DatagramSlotCount;
            _recordsPerSlot = Math.Max(1, _slotBytes / 16);
            _bytes = new byte[_slotBytes * _slotCount];
            _records = new OscResolvedMessage[_recordsPerSlot * _slotCount];
            _headers = new SlotHeader[_slotCount];
            _remotes = new EndPoint[_slotCount];
        }

        public int SlotBytes => _slotBytes;
        public int SlotCount => _slotCount;
        public int RecordsPerSlot => _recordsPerSlot;
        public int PendingDatagramCount
        {
            get { lock (_sync) return checked((int)(_committedTail - _head)); }
        }

        public bool TryReserveSlot(out int slot)
        {
            lock (_sync)
            {
                if (_reservedTail != _committedTail) { slot = -1; return false; }
                if (_reservedTail - _head == _slotCount)
                {
                    int dropped = Physical(_head);
                    _headers[dropped].State = SlotState.Free;
                    _remotes[dropped] = null;
                    _head++;
                    _diagnostics.IncrementDroppedDatagrams();
                }

                long logical = _reservedTail++;
                slot = Physical(logical);
                _headers[slot].State = SlotState.Reserved;
                return true;
            }
        }

        public Span<byte> GetSlotBytes(int slot)
        {
            ValidateReserved(slot);
            return new Span<byte>(_bytes, slot * _slotBytes, _slotBytes);
        }

        /// <summary>
        /// 予約済みスロットの backing 配列とオフセットを返す。
        /// <c>Socket.Receive(byte[], int, int, SocketFlags)</c> に渡すための API。
        /// Unity 6000.3.19f1 Mono では <c>Socket.Receive(Span&lt;byte&gt;, SocketFlags)</c> が
        /// 呼び出しごとにスロット長の一時配列を確保する（2026-09-15 実測: 2048 byte スロットで 2080 byte/回）ため、
        /// 受信ループは Span ではなくこの配列オーバーロードを使う。
        /// </summary>
        public ArraySegment<byte> GetSlotSegment(int slot)
        {
            ValidateReserved(slot);
            return new ArraySegment<byte>(_bytes, slot * _slotBytes, _slotBytes);
        }

        public Span<OscResolvedMessage> GetSlotRecords(int slot)
        {
            ValidateReserved(slot);
            return new Span<OscResolvedMessage>(_records, slot * _recordsPerSlot, _recordsPerSlot);
        }

        /// <summary>
        /// 予約済みスロットのデータグラムの送信元を記録する（null で消す）。<see cref="Commit"/> の前に呼ぶ。
        /// </summary>
        public void SetRemoteEndPoint(int slot, EndPoint remote)
        {
            ValidateReserved(slot);
            _remotes[slot] = remote;
        }

        public void Commit(int slot, int length, int recordCount, int tableVersion)
        {
            if (length < 0 || length > _slotBytes) throw new ArgumentOutOfRangeException(nameof(length));
            if (recordCount < 0 || recordCount > _recordsPerSlot) throw new ArgumentOutOfRangeException(nameof(recordCount));
            lock (_sync)
            {
                long logical = _reservedTail - 1;
                ValidateReservedLocked(slot, logical);
                SlotHeader header = _headers[slot];
                header.Length = length;
                header.RecordCount = recordCount;
                header.TableVersion = tableVersion;
                header.Sequence = ++_sequence;
                header.State = SlotState.Committed;
                _headers[slot] = header;
                _committedTail++;
            }
        }

        public void Abort(int slot)
        {
            lock (_sync)
            {
                long logical = _reservedTail - 1;
                ValidateReservedLocked(slot, logical);
                _headers[slot].State = SlotState.Free;
                _remotes[slot] = null;
                _reservedTail--;
            }
        }

        public int Drain(OscDrainBuffer target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            lock (_sync)
            {
                target.Reset();
                for (long logical = _head; logical < _committedTail; logical++)
                {
                    int slot = Physical(logical);
                    SlotHeader header = _headers[slot];
                    target.Append(new ReadOnlySpan<byte>(_bytes, slot * _slotBytes, header.Length),
                        new ReadOnlySpan<OscResolvedMessage>(_records, slot * _recordsPerSlot, header.RecordCount),
                        _remotes[slot]);
                    _remotes[slot] = null;
                    _headers[slot].State = SlotState.Free;
                }
                int count = checked((int)(_committedTail - _head));
                _head = _committedTail;
                return count;
            }
        }

        public void CommitExternal(ReadOnlySpan<byte> datagram, OscAddressKeyTable table)
        {
            if (datagram.Length > _slotBytes) { _diagnostics.IncrementOversizedDatagrams(); return; }
            if (!TryReserveSlot(out int slot)) return;
            try
            {
                datagram.CopyTo(GetSlotBytes(slot));
                Span<OscResolvedMessage> records = GetSlotRecords(slot);
                int count = OscMessageClassifier.ParseAndClassify(
                    GetSlotBytes(slot).Slice(0, datagram.Length), table, records, _diagnostics);
                Commit(slot, datagram.Length, count, table == null ? 0 : table.Version);
            }
            catch
            {
                Abort(slot);
                throw;
            }
        }

        internal void ParseAndCommit(int slot, int length, OscAddressKeyTable table)
        {
            if (table == null) throw new ArgumentNullException(nameof(table));
            try
            {
                Span<OscResolvedMessage> records = GetSlotRecords(slot);
                int count = OscMessageClassifier.ParseAndClassify(
                    GetSlotBytes(slot).Slice(0, length), table, records, _diagnostics);
                Commit(slot, length, count, table.Version);
            }
            catch
            {
                Abort(slot);
                throw;
            }
        }

        public void Clear()
        {
            lock (_sync)
            {
                if (_reservedTail != _committedTail) throw new InvalidOperationException("Cannot clear while a slot is reserved.");
                for (int i = 0; i < _headers.Length; i++) _headers[i].State = SlotState.Free;
                Array.Clear(_remotes, 0, _remotes.Length);
                _head = _committedTail = _reservedTail;
            }
        }

        private static int Physical(long logical, int count) => (int)(logical % count);
        private int Physical(long logical) => Physical(logical, _slotCount);

        private void ValidateReserved(int slot)
        {
            lock (_sync) ValidateReservedLocked(slot, _reservedTail - 1);
        }

        private void ValidateReservedLocked(int slot, long logical)
        {
            if ((uint)slot >= (uint)_slotCount || logical < _committedTail || Physical(logical) != slot ||
                _headers[slot].State != SlotState.Reserved)
                throw new InvalidOperationException("The slot is not the current reservation.");
        }
    }
}
