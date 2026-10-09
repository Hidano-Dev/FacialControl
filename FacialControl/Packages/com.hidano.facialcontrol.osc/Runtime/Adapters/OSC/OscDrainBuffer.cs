using System;
using System.Net;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>固定容量のメインスレッド所有ドレイン先。</summary>
    public sealed class OscDrainBuffer
    {
        private readonly byte[] _bytes;
        private readonly OscResolvedMessage[] _records;
        private readonly int[] _lengths;
        private readonly EndPoint[] _remotes;
        private readonly int _slotBytes;

        public OscDrainBuffer(in OscReceiveOptions options)
        {
            _slotBytes = options.DatagramSlotBytes;
            _bytes = new byte[options.DatagramSlotBytes * options.DatagramSlotCount];
            _records = new OscResolvedMessage[options.DatagramSlotCount * RecordsPerSlot(options.DatagramSlotBytes)];
            _lengths = new int[options.DatagramSlotCount];
            _remotes = new EndPoint[options.DatagramSlotCount];
        }

        private static int RecordsPerSlot(int slotBytes) => Math.Max(1, slotBytes / 16);

        public int DatagramCount { get; private set; }
        public int RecordCount { get; private set; }

        public ReadOnlySpan<byte> GetDatagram(int index)
        {
            if ((uint)index >= (uint)DatagramCount) throw new ArgumentOutOfRangeException(nameof(index));
            int offset = index * _slotBytes;
            return new ReadOnlySpan<byte>(_bytes, offset, _lengths[index]);
        }

        public ref readonly OscResolvedMessage GetRecord(int index)
        {
            if ((uint)index >= (uint)RecordCount) throw new ArgumentOutOfRangeException(nameof(index));
            return ref _records[index];
        }

        /// <summary>
        /// <paramref name="recordIndex"/> のメッセージが入っていたデータグラムの送信元。受信ループが送信元を
        /// 取っていなかったときは null。
        /// </summary>
        public EndPoint GetRemoteEndPoint(int recordIndex)
        {
            ref readonly OscResolvedMessage record = ref GetRecord(recordIndex);
            int datagramIndex = record.ElementOffset / _slotBytes;
            return (uint)datagramIndex < (uint)DatagramCount ? _remotes[datagramIndex] : null;
        }

        public OscMessageView GetView(int index)
        {
            ref readonly OscResolvedMessage record = ref GetRecord(index);
            if (record.ElementOffset < 0 || record.ElementLength < 0 ||
                record.ElementOffset > _bytes.Length - record.ElementLength)
                throw new InvalidOperationException("The record element is outside the drain buffer.");

            ReadOnlySpan<byte> element = new ReadOnlySpan<byte>(_bytes, record.ElementOffset, record.ElementLength);
            var reader = new OscPacketReader(element);
            if (!reader.TryReadNext(out OscMessageView view))
                return default;
            return new OscMessageView(view.Address, view.TypeTags, view.Arguments, view.Element,
                record.TimestampKey, record.ElementOffset);
        }

        internal void Append(ReadOnlySpan<byte> datagram, ReadOnlySpan<OscResolvedMessage> records, EndPoint remote = null)
        {
            if (datagram.Length > _slotBytes || records.Length > _records.Length - RecordCount)
                throw new InvalidOperationException("The drain buffer capacity was exceeded.");

            int byteOffset = DatagramCount * _slotBytes;
            datagram.CopyTo(new Span<byte>(_bytes, byteOffset, datagram.Length));
            _lengths[DatagramCount] = datagram.Length;
            _remotes[DatagramCount] = remote;
            int recordOffset = RecordCount;
            for (int i = 0; i < records.Length; i++)
            {
                OscResolvedMessage source = records[i];
                _records[recordOffset + i] = new OscResolvedMessage(
                    source.Kind, source.Control, source.HasFloat, source.FloatValue,
                    source.MappingIndex, source.GazeRouteSet, source.ListenerSlot, source.TimestampKey,
                    source.TableVersion, byteOffset + source.ElementOffset, source.ElementLength,
                    source.SenderUuid, source.SenderStartedAtUnixMs, source.SenderIdentityValid);
            }
            DatagramCount++;
            RecordCount += records.Length;
        }

        public void Reset()
        {
            Array.Clear(_remotes, 0, DatagramCount);
            DatagramCount = 0;
            RecordCount = 0;
        }
    }
}
