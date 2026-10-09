using System;
using System.Buffers;
using System.Text;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.OSC
{
    public readonly struct OscEncodedFloat
    {
        public readonly byte[] AddressUtf8;
        public readonly float Value;

        public OscEncodedFloat(byte[] addressUtf8, float value)
        {
            AddressUtf8 = addressUtf8 ?? throw new ArgumentNullException(nameof(addressUtf8));
            Value = value;
        }
    }

    public readonly struct OscBundlePacket
    {
        public readonly byte[] Buffer;
        public readonly int Length;
        public readonly ulong Timestamp;
        public readonly int MessageCount;

        public OscBundlePacket(byte[] buffer, int length, ulong timestamp, int messageCount)
        {
            Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            Length = length;
            Timestamp = timestamp;
            MessageCount = messageCount;
        }

        public ReadOnlySpan<byte> Span => new ReadOnlySpan<byte>(Buffer, 0, Length);
    }

    public sealed class OscBundleBuilder : IDisposable
    {
        public const int DefaultMaxPacketSize = 1472;
        public const int BundleHeaderSize = 16;

        private const int DefaultPacketCapacity = 64;
        private const int FloatMessageTypeCount = 1;
        private const int SenderIdentityMessageTypeCount = 2;
        private const int PresetBaseMessageTypeCount = 1;
        private const int PresetCustomMessageTypeCount = 2;
        private const string PresetCustom = "custom";
        private const byte TypeBlob = (byte)'b';
        private const byte TypeFloat = (byte)'f';
        private const byte TypeString = (byte)'s';

        private static readonly byte[] BundleIdentifierBytes =
        {
            (byte)'#', (byte)'b', (byte)'u', (byte)'n', (byte)'d', (byte)'l', (byte)'e'
        };

        private readonly ArrayPool<byte> _bytePool;
        private readonly int _maxPacketSize;

        private byte[][] _buffers;
        private int[] _lengths;
        private int[] _messageCounts;
        private ulong[] _timestamps;
        private int _packetCount;
        private bool _disposed;
        private bool _splitInCurrentBuild;
        private bool _hasLoggedMtuSplitWarning;
        private byte[] _frameSenderIdentityAddressUtf8;
        private byte[] _frameSenderUuidBytes;
        private string _frameStartedAtUnixMs;

        public OscBundleBuilder(
            int maxPacketSize = DefaultMaxPacketSize,
            int initialPacketCapacity = DefaultPacketCapacity,
            ArrayPool<byte> bytePool = null)
        {
            if (maxPacketSize <= BundleHeaderSize + 4)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPacketSize),
                    maxPacketSize,
                    "OSC bundle packet size must be larger than the bundle header.");
            }

            if (initialPacketCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialPacketCapacity));
            }

            _maxPacketSize = maxPacketSize;
            _bytePool = bytePool ?? ArrayPool<byte>.Shared;
            _buffers = new byte[initialPacketCapacity][];
            _lengths = new int[initialPacketCapacity];
            _messageCounts = new int[initialPacketCapacity];
            _timestamps = new ulong[initialPacketCapacity];
        }

        public int MaxPacketSize => _maxPacketSize;

        public int PacketCount => _packetCount;

        public int ContinuationCount => _packetCount > 0 ? _packetCount - 1 : 0;

        public OscBundlePacket GetPacket(int index)
        {
            ThrowIfDisposed();

            if ((uint)index >= (uint)_packetCount)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return new OscBundlePacket(
                _buffers[index],
                _lengths[index],
                _timestamps[index],
                _messageCounts[index]);
        }

        public ReadOnlySpan<byte> GetPacketSpan(int index)
        {
            OscBundlePacket packet = GetPacket(index);
            return packet.Span;
        }

        public int BuildFloatBundle(ulong timestamp, ReadOnlySpan<OscEncodedFloat> messages)
        {
            ThrowIfDisposed();
            ResetBuildState();

            if (messages.Length == 0)
            {
                return 0;
            }

            BeginPacket(timestamp);
            for (int i = 0; i < messages.Length; i++)
            {
                OscEncodedFloat message = messages[i];
                ValidateAddress(message.AddressUtf8, nameof(messages));
                AddFloatMessage(timestamp, message.AddressUtf8, message.Value);
            }

            LogMtuSplitIfNeeded();
            return _packetCount;
        }

        public int BuildFloatBundle(
            ulong timestamp,
            byte[][] addressUtf8,
            float[] values,
            int count)
        {
            ThrowIfDisposed();

            if (addressUtf8 == null)
            {
                throw new ArgumentNullException(nameof(addressUtf8));
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (count < 0 || count > addressUtf8.Length || count > values.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            ResetBuildState();
            if (count == 0)
            {
                return 0;
            }

            BeginPacket(timestamp);
            for (int i = 0; i < count; i++)
            {
                byte[] address = addressUtf8[i];
                ValidateAddress(address, nameof(addressUtf8));
                AddFloatMessage(timestamp, address, values[i]);
            }

            LogMtuSplitIfNeeded();
            return _packetCount;
        }

        public int BuildFrameBundle(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] floatAddressUtf8,
            float[] floatValues,
            int floatCount)
        {
            return BuildFrameBundle(
                timestamp,
                senderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                floatAddressUtf8,
                floatValues,
                floatCount,
                heartbeatAddressUtf8: null,
                heartbeatNames: null,
                heartbeatNameCount: 0);
        }

        public int BuildFrameBundle(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] floatAddressUtf8,
            float[] floatValues,
            int floatCount,
            byte[] heartbeatAddressUtf8,
            string[] heartbeatNames,
            int heartbeatNameCount,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix)
        {
            return BuildFrameBundleCore(
                timestamp,
                senderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                floatAddressUtf8,
                floatValues,
                floatCount,
                heartbeatAddressUtf8,
                heartbeatNames,
                heartbeatNameCount,
                presetAddressUtf8,
                presetName,
                customPrefix,
                gazeAdvertisementAddressUtf8: null,
                gazeAdvertisementPairs: null,
                gazeAdvertisementPairCount: 0);
        }

        public int BuildFrameBundle(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] floatAddressUtf8,
            float[] floatValues,
            int floatCount,
            byte[] heartbeatAddressUtf8,
            string[] heartbeatNames,
            int heartbeatNameCount,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix,
            byte[] gazeAdvertisementAddressUtf8,
            string[] gazeAdvertisementPairs,
            int gazeAdvertisementPairCount)
        {
            return BuildFrameBundleCore(
                timestamp,
                senderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                floatAddressUtf8,
                floatValues,
                floatCount,
                heartbeatAddressUtf8,
                heartbeatNames,
                heartbeatNameCount,
                presetAddressUtf8,
                presetName,
                customPrefix,
                gazeAdvertisementAddressUtf8,
                gazeAdvertisementPairs,
                gazeAdvertisementPairCount);
        }

        public int BuildFrameBundle(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] floatAddressUtf8,
            float[] floatValues,
            int floatCount,
            byte[] heartbeatAddressUtf8,
            string[] heartbeatNames,
            int heartbeatNameCount)
        {
            return BuildFrameBundleCore(
                timestamp,
                senderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                floatAddressUtf8,
                floatValues,
                floatCount,
                heartbeatAddressUtf8,
                heartbeatNames,
                heartbeatNameCount,
                presetAddressUtf8: null,
                presetName: null,
                customPrefix: null,
                gazeAdvertisementAddressUtf8: null,
                gazeAdvertisementPairs: null,
                gazeAdvertisementPairCount: 0);
        }

        private int BuildFrameBundleCore(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            byte[][] floatAddressUtf8,
            float[] floatValues,
            int floatCount,
            byte[] heartbeatAddressUtf8,
            string[] heartbeatNames,
            int heartbeatNameCount,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix,
            byte[] gazeAdvertisementAddressUtf8,
            string[] gazeAdvertisementPairs,
            int gazeAdvertisementPairCount)
        {
            ThrowIfDisposed();
            ValidateAddress(senderIdentityAddressUtf8, nameof(senderIdentityAddressUtf8));

            if (senderUuidBytes == null)
            {
                throw new ArgumentNullException(nameof(senderUuidBytes));
            }

            if (startedAtUnixMs == null)
            {
                throw new ArgumentNullException(nameof(startedAtUnixMs));
            }

            if (floatAddressUtf8 == null)
            {
                throw new ArgumentNullException(nameof(floatAddressUtf8));
            }

            if (floatValues == null)
            {
                throw new ArgumentNullException(nameof(floatValues));
            }

            if (floatCount < 0 || floatCount > floatAddressUtf8.Length || floatCount > floatValues.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(floatCount));
            }

            bool includeHeartbeat = heartbeatAddressUtf8 != null;
            if (includeHeartbeat)
            {
                ValidateAddress(heartbeatAddressUtf8, nameof(heartbeatAddressUtf8));

                if (heartbeatNames == null)
                {
                    throw new ArgumentNullException(nameof(heartbeatNames));
                }

                if (heartbeatNameCount < 0 || heartbeatNameCount > heartbeatNames.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(heartbeatNameCount));
                }
            }
            else if (heartbeatNames != null || heartbeatNameCount != 0)
            {
                throw new ArgumentException(
                    "Heartbeat address must be provided when heartbeat names are provided.",
                    nameof(heartbeatAddressUtf8));
            }

            bool includePreset = presetAddressUtf8 != null;
            if (includePreset)
            {
                ValidateAddress(presetAddressUtf8, nameof(presetAddressUtf8));
                ValidatePresetName(presetName, nameof(presetName));
            }
            else if (presetName != null || customPrefix != null)
            {
                throw new ArgumentException(
                    "Preset address must be provided when preset payload is provided.",
                    nameof(presetAddressUtf8));
            }

            bool includeGazeAdvertisement = gazeAdvertisementAddressUtf8 != null;
            if (includeGazeAdvertisement)
            {
                ValidateAddress(gazeAdvertisementAddressUtf8, nameof(gazeAdvertisementAddressUtf8));
                if (gazeAdvertisementPairs == null)
                {
                    throw new ArgumentNullException(nameof(gazeAdvertisementPairs));
                }

                if (gazeAdvertisementPairCount < 0 ||
                    gazeAdvertisementPairCount > gazeAdvertisementPairs.Length / 2)
                {
                    throw new ArgumentOutOfRangeException(nameof(gazeAdvertisementPairCount));
                }

                for (int i = 0; i < gazeAdvertisementPairCount * 2; i++)
                {
                    if (string.IsNullOrEmpty(gazeAdvertisementPairs[i]))
                    {
                        throw new ArgumentException(
                            "Gaze advertisement pairs must contain non-empty strings.",
                            nameof(gazeAdvertisementPairs));
                    }
                }
            }
            else if (gazeAdvertisementPairs != null || gazeAdvertisementPairCount != 0)
            {
                throw new ArgumentException(
                    "Gaze advertisement address must be provided when advertisement pairs are provided.",
                    nameof(gazeAdvertisementAddressUtf8));
            }

            ResetBuildState();
            SetFrameSplitSenderIdentity(senderIdentityAddressUtf8, senderUuidBytes, startedAtUnixMs);
            try
            {
                BeginPacket(timestamp);
                AddSenderIdentityMessage(timestamp, senderIdentityAddressUtf8, senderUuidBytes, startedAtUnixMs);

                for (int i = 0; i < floatCount; i++)
                {
                    byte[] address = floatAddressUtf8[i];
                    ValidateAddress(address, nameof(floatAddressUtf8));
                    AddFloatMessage(timestamp, address, floatValues[i]);
                }

                if (includeHeartbeat)
                {
                    AddHeartbeatMessages(timestamp, heartbeatAddressUtf8, heartbeatNames, heartbeatNameCount);
                }

                if (includePreset)
                {
                    AddPresetMessage(timestamp, presetAddressUtf8, presetName, customPrefix);
                }

                if (includeGazeAdvertisement && gazeAdvertisementPairCount > 0)
                {
                    AddGazeAdvertisementMessages(
                        timestamp,
                        gazeAdvertisementAddressUtf8,
                        gazeAdvertisementPairs,
                        gazeAdvertisementPairCount);
                }

                LogMtuSplitIfNeeded();
                return _packetCount;
            }
            finally
            {
                ClearFrameSplitSenderIdentity();
            }
        }

        /// <summary>
        /// 送信元識別と値フレーム（<c>/_facialcontrol/values</c>）だけのパケットを組み立て、パケット総数を返す。
        /// 分け方は <see cref="AppendIndexedValuesPackets"/> と同じ。ヒープ確保をしない。
        /// </summary>
        public int BuildIndexedValuesPackets(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            int layoutVersion,
            ReadOnlySpan<float> slotValues)
        {
            ThrowIfDisposed();
            ResetBuildState();
            return AppendIndexedValuesPackets(
                timestamp,
                senderIdentityAddressUtf8,
                senderUuidBytes,
                startedAtUnixMs,
                layoutVersion,
                slotValues);
        }

        /// <summary>
        /// 直前に組み立てたパケットの後ろに、送信元識別と値フレーム（<c>/_facialcontrol/values</c>）を載せた
        /// パケットを同じ <paramref name="timestamp"/> で追加し、パケット総数を返す。値フレームは 1 通が
        /// <see cref="OscIndexedFrameCodec.DefaultMaxMessageBytes"/> に収まるよう slot の offset 付きで分け、
        /// 1 パケットに 1 通ずつ載せる（slot が 0 個でも 1 通送り、受信側がバージョンを知れるようにする）。
        /// ヒープ確保をしない。
        /// </summary>
        public int AppendIndexedValuesPackets(
            ulong timestamp,
            byte[] senderIdentityAddressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs,
            int layoutVersion,
            ReadOnlySpan<float> slotValues)
        {
            ThrowIfDisposed();
            ValidateAddress(senderIdentityAddressUtf8, nameof(senderIdentityAddressUtf8));
            if (senderUuidBytes == null)
            {
                throw new ArgumentNullException(nameof(senderUuidBytes));
            }

            if (startedAtUnixMs == null)
            {
                throw new ArgumentNullException(nameof(startedAtUnixMs));
            }

            int senderIdentityElementSize = 4 + GetSenderIdentityMessageSize(
                senderIdentityAddressUtf8.Length,
                senderUuidBytes.Length,
                startedAtUnixMs);
            int maxValuesMessageBytes = Math.Min(
                OscIndexedFrameCodec.DefaultMaxMessageBytes,
                _maxPacketSize - BundleHeaderSize - senderIdentityElementSize - 4);
            int maxValuesPerMessage = OscIndexedFrameCodec.GetMaxValuesPerMessage(maxValuesMessageBytes);
            if (maxValuesPerMessage <= 0)
            {
                throw new InvalidOperationException(
                    "The configured packet size cannot hold a values frame together with the sender identity.");
            }

            int messageCount = OscIndexedFrameCodec.GetValuesMessageCount(slotValues.Length, maxValuesPerMessage);
            for (int i = 0; i < messageCount; i++)
            {
                int offset = i * maxValuesPerMessage;
                int count = Math.Min(maxValuesPerMessage, slotValues.Length - offset);
                BeginPacket(timestamp);
                AddSenderIdentityMessage(timestamp, senderIdentityAddressUtf8, senderUuidBytes, startedAtUnixMs);
                AddValuesMessage(timestamp, layoutVersion, offset, slotValues.Slice(offset, count));
            }

            return _packetCount;
        }

        public int BuildHeartbeatBundle(
            ulong timestamp,
            byte[] addressUtf8,
            ReadOnlySpan<string> names)
        {
            return BuildHeartbeatBundle(
                timestamp,
                addressUtf8,
                names,
                presetAddressUtf8: null,
                presetName: null,
                customPrefix: null);
        }

        public int BuildHeartbeatBundle(
            ulong timestamp,
            byte[] addressUtf8,
            ReadOnlySpan<string> names,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix)
        {
            ThrowIfDisposed();
            ValidateAddress(addressUtf8, nameof(addressUtf8));
            bool includePreset = presetAddressUtf8 != null;
            if (includePreset)
            {
                ValidateAddress(presetAddressUtf8, nameof(presetAddressUtf8));
                ValidatePresetName(presetName, nameof(presetName));
            }
            else if (presetName != null || customPrefix != null)
            {
                throw new ArgumentException(
                    "Preset address must be provided when preset payload is provided.",
                    nameof(presetAddressUtf8));
            }

            ResetBuildState();
            BeginPacket(timestamp);

            if (names.Length == 0)
            {
                AddStringMessage(timestamp, addressUtf8, ReadOnlySpan<string>.Empty);
                if (includePreset)
                {
                    AddPresetMessage(timestamp, presetAddressUtf8, presetName, customPrefix);
                }

                LogMtuSplitIfNeeded();
                return _packetCount;
            }

            int index = 0;
            while (index < names.Length)
            {
                int chunkCount = GetFittingStringChunkCount(addressUtf8.Length, names, index);
                if (chunkCount <= 0)
                {
                    throw new InvalidOperationException(
                        "A single OSC heartbeat string message exceeds the configured packet size.");
                }

                ReadOnlySpan<string> chunk = names.Slice(index, chunkCount);
                AddStringMessage(timestamp, addressUtf8, chunk);
                index += chunkCount;
            }

            if (includePreset)
            {
                AddPresetMessage(timestamp, presetAddressUtf8, presetName, customPrefix);
            }

            LogMtuSplitIfNeeded();
            return _packetCount;
        }

        public int BuildHeartbeatBundle(
            ulong timestamp,
            byte[] addressUtf8,
            string[] names,
            int count)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            if (count < 0 || count > names.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return BuildHeartbeatBundle(timestamp, addressUtf8, new ReadOnlySpan<string>(names, 0, count));
        }

        public int BuildHeartbeatBundle(
            ulong timestamp,
            byte[] addressUtf8,
            string[] names,
            int count,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix)
        {
            if (names == null)
            {
                throw new ArgumentNullException(nameof(names));
            }

            if (count < 0 || count > names.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            return BuildHeartbeatBundle(
                timestamp,
                addressUtf8,
                new ReadOnlySpan<string>(names, 0, count),
                presetAddressUtf8,
                presetName,
                customPrefix);
        }

        public int BuildPresetBundle(
            ulong timestamp,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix)
        {
            ThrowIfDisposed();
            ValidateAddress(presetAddressUtf8, nameof(presetAddressUtf8));
            ValidatePresetName(presetName, nameof(presetName));

            ResetBuildState();
            BeginPacket(timestamp);
            AddPresetMessage(timestamp, presetAddressUtf8, presetName, customPrefix);
            return _packetCount;
        }

        private void AddHeartbeatMessages(
            ulong timestamp,
            byte[] addressUtf8,
            string[] names,
            int count)
        {
            if (count == 0)
            {
                AddStringMessage(timestamp, addressUtf8, ReadOnlySpan<string>.Empty);
                return;
            }

            ReadOnlySpan<string> nameSpan = new ReadOnlySpan<string>(names, 0, count);
            int index = 0;
            while (index < nameSpan.Length)
            {
                int chunkCount = GetFittingStringChunkCount(addressUtf8.Length, nameSpan, index);
                if (chunkCount <= 0)
                {
                    throw new InvalidOperationException(
                        "A single OSC heartbeat string message exceeds the configured packet size.");
                }

                AddStringMessage(timestamp, addressUtf8, nameSpan.Slice(index, chunkCount));
                index += chunkCount;
            }
        }

        private void AddGazeAdvertisementMessages(
            ulong timestamp,
            byte[] addressUtf8,
            string[] pairs,
            int pairCount)
        {
            ReadOnlySpan<string> pairSpan = new ReadOnlySpan<string>(pairs, 0, pairCount * 2);
            int index = 0;
            while (index < pairSpan.Length)
            {
                int chunkPairCount = GetFittingGazeAdvertisementPairCount(
                    addressUtf8.Length,
                    pairSpan,
                    index);
                if (chunkPairCount <= 0)
                {
                    throw new InvalidOperationException(
                        "A single OSC gaze advertisement pair message exceeds the configured packet size.");
                }

                AddStringMessage(
                    timestamp,
                    addressUtf8,
                    pairSpan.Slice(index, chunkPairCount * 2));
                index += chunkPairCount * 2;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            for (int i = 0; i < _buffers.Length; i++)
            {
                byte[] buffer = _buffers[i];
                if (buffer != null)
                {
                    _bytePool.Return(buffer);
                    _buffers[i] = null;
                }
            }

            _packetCount = 0;
            _disposed = true;
        }

        private void ResetBuildState()
        {
            _packetCount = 0;
            _splitInCurrentBuild = false;
            ClearFrameSplitSenderIdentity();
        }

        private void BeginPacket(ulong timestamp)
        {
            int index = _packetCount;
            EnsurePacketSlot(index);
            EnsurePacketBuffer(index);

            _timestamps[index] = timestamp;
            _messageCounts[index] = 0;
            _lengths[index] = 0;
            WriteBundleHeader(index, timestamp);
            _packetCount++;
        }

        private void AddFloatMessage(ulong timestamp, byte[] addressUtf8, float value)
        {
            int messageSize = GetFloatMessageSize(addressUtf8.Length);
            BeginMessage(timestamp, messageSize, out int packetIndex, out int messageStart);
            WriteFloatMessage(packetIndex, addressUtf8, value);
            CompleteMessage(packetIndex, messageStart, messageSize);
        }

        private void AddSenderIdentityMessage(
            ulong timestamp,
            byte[] addressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs)
        {
            int messageSize = GetSenderIdentityMessageSize(
                addressUtf8.Length,
                senderUuidBytes.Length,
                startedAtUnixMs);
            BeginMessage(timestamp, messageSize, out int packetIndex, out int messageStart);
            WriteSenderIdentityMessage(packetIndex, addressUtf8, senderUuidBytes, startedAtUnixMs);
            CompleteMessage(packetIndex, messageStart, messageSize);
        }

        private void AddValuesMessage(ulong timestamp, int layoutVersion, int offset, ReadOnlySpan<float> values)
        {
            int messageSize = OscIndexedFrameCodec.GetValuesMessageSize(values.Length);
            BeginMessage(timestamp, messageSize, out int packetIndex, out int messageStart);
            int written = OscIndexedFrameCodec.WriteValuesMessage(
                new Span<byte>(_buffers[packetIndex], messageStart, messageSize),
                layoutVersion,
                offset,
                values);
            _lengths[packetIndex] = messageStart + written;
            CompleteMessage(packetIndex, messageStart, messageSize);
        }

        private void AddStringMessage(ulong timestamp, byte[] addressUtf8, ReadOnlySpan<string> values)
        {
            int messageSize = GetStringMessageSize(addressUtf8.Length, values);
            BeginMessage(timestamp, messageSize, out int packetIndex, out int messageStart);
            WriteStringMessage(packetIndex, addressUtf8, values);
            CompleteMessage(packetIndex, messageStart, messageSize);
        }

        private void AddPresetMessage(
            ulong timestamp,
            byte[] presetAddressUtf8,
            string presetName,
            string customPrefix)
        {
            bool includeCustomPrefix = ShouldIncludeCustomPrefix(presetName, customPrefix);
            int messageSize = GetPresetMessageSize(presetAddressUtf8.Length, presetName, includeCustomPrefix, customPrefix);
            BeginMessage(timestamp, messageSize, out int packetIndex, out int messageStart);
            WritePresetMessage(packetIndex, presetAddressUtf8, presetName, includeCustomPrefix, customPrefix);
            CompleteMessage(packetIndex, messageStart, messageSize);
        }

        private void BeginMessage(ulong timestamp, int messageSize, out int packetIndex, out int messageStart)
        {
            int elementSize = 4 + messageSize;
            if (elementSize > _maxPacketSize - BundleHeaderSize)
            {
                throw new InvalidOperationException(
                    "A single OSC bundle element exceeds the configured packet size.");
            }

            packetIndex = _packetCount - 1;
            if (_lengths[packetIndex] + elementSize > _maxPacketSize)
            {
                if (_messageCounts[packetIndex] == 0)
                {
                    throw new InvalidOperationException(
                        "A single OSC bundle element exceeds the configured packet size.");
                }

                _splitInCurrentBuild = true;
                BeginPacket(timestamp);
                packetIndex = _packetCount - 1;
                AddFrameSenderIdentityToSplitPacket(timestamp);
                packetIndex = _packetCount - 1;
                if (_lengths[packetIndex] + elementSize > _maxPacketSize)
                {
                    throw new InvalidOperationException(
                        "A single OSC bundle element plus sender identity exceeds the configured packet size.");
                }
            }

            int offset = _lengths[packetIndex];
            WriteInt32BigEndian(_buffers[packetIndex], ref offset, messageSize);
            messageStart = offset;
            _lengths[packetIndex] = offset;
        }

        private void CompleteMessage(int packetIndex, int messageStart, int messageSize)
        {
            int written = _lengths[packetIndex] - messageStart;
            if (written != messageSize)
            {
                throw new InvalidOperationException("OSC message writer produced an unexpected byte count.");
            }

            _messageCounts[packetIndex]++;
        }

        private void WriteBundleHeader(int packetIndex, ulong timestamp)
        {
            byte[] buffer = _buffers[packetIndex];
            int offset = 0;
            WriteOscString(buffer, ref offset, BundleIdentifierBytes);
            WriteUInt64BigEndian(buffer, ref offset, timestamp);
            _lengths[packetIndex] = offset;
        }

        private void WriteFloatMessage(int packetIndex, byte[] addressUtf8, float value)
        {
            byte[] buffer = _buffers[packetIndex];
            int offset = _lengths[packetIndex];

            WriteOscString(buffer, ref offset, addressUtf8);
            WriteTypeTags(buffer, ref offset, TypeFloat, FloatMessageTypeCount);
            WriteFloatBigEndian(buffer, ref offset, value);

            _lengths[packetIndex] = offset;
        }

        private void WriteSenderIdentityMessage(
            int packetIndex,
            byte[] addressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs)
        {
            byte[] buffer = _buffers[packetIndex];
            int offset = _lengths[packetIndex];

            WriteOscString(buffer, ref offset, addressUtf8);
            WriteTypeTags(buffer, ref offset, TypeBlob, TypeString);
            WriteBlob(buffer, ref offset, senderUuidBytes);
            WriteOscString(buffer, ref offset, startedAtUnixMs);

            _lengths[packetIndex] = offset;
        }

        private void WriteStringMessage(int packetIndex, byte[] addressUtf8, ReadOnlySpan<string> values)
        {
            byte[] buffer = _buffers[packetIndex];
            int offset = _lengths[packetIndex];

            WriteOscString(buffer, ref offset, addressUtf8);
            WriteTypeTags(buffer, ref offset, TypeString, values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                WriteOscString(buffer, ref offset, values[i]);
            }

            _lengths[packetIndex] = offset;
        }

        private void WritePresetMessage(
            int packetIndex,
            byte[] presetAddressUtf8,
            string presetName,
            bool includeCustomPrefix,
            string customPrefix)
        {
            byte[] buffer = _buffers[packetIndex];
            int offset = _lengths[packetIndex];

            WriteOscString(buffer, ref offset, presetAddressUtf8);
            WriteTypeTags(
                buffer,
                ref offset,
                TypeString,
                includeCustomPrefix ? PresetCustomMessageTypeCount : PresetBaseMessageTypeCount);
            WriteOscString(buffer, ref offset, presetName);
            if (includeCustomPrefix)
            {
                WriteOscString(buffer, ref offset, customPrefix);
            }

            _lengths[packetIndex] = offset;
        }

        private int GetFittingStringChunkCount(
            int addressByteCount,
            ReadOnlySpan<string> values,
            int startIndex)
        {
            int count = 0;
            int stringsSize = 0;
            int maxElementPayloadSize = _maxPacketSize
                - BundleHeaderSize
                - 4
                - GetFrameSplitSenderIdentityElementSize();

            for (int i = startIndex; i < values.Length; i++)
            {
                int nextStringSize = GetOscStringSize(GetUtf8ByteCount(values[i]));
                int nextCount = count + 1;
                int nextMessageSize =
                    GetOscStringSize(addressByteCount) +
                    GetOscStringSize(1 + nextCount) +
                    stringsSize +
                    nextStringSize;

                if (nextMessageSize > maxElementPayloadSize)
                {
                    break;
                }

                stringsSize += nextStringSize;
                count = nextCount;
            }

            return count;
        }

        private int GetFittingGazeAdvertisementPairCount(
            int addressByteCount,
            ReadOnlySpan<string> values,
            int startIndex)
        {
            int pairCount = 0;
            int stringsSize = 0;
            int maxElementPayloadSize = _maxPacketSize
                - BundleHeaderSize
                - 4
                - GetFrameSplitSenderIdentityElementSize();

            for (int i = startIndex; i < values.Length; i += 2)
            {
                int nextPairCount = pairCount + 1;
                int nextStringsSize = stringsSize
                    + GetOscStringSize(GetUtf8ByteCount(values[i]))
                    + GetOscStringSize(GetUtf8ByteCount(values[i + 1]));
                int nextMessageSize =
                    GetOscStringSize(addressByteCount)
                    + GetOscStringSize(1 + (nextPairCount * 2))
                    + nextStringsSize;

                if (nextMessageSize > maxElementPayloadSize)
                {
                    break;
                }

                stringsSize = nextStringsSize;
                pairCount = nextPairCount;
            }

            return pairCount;
        }

        private void SetFrameSplitSenderIdentity(
            byte[] addressUtf8,
            byte[] senderUuidBytes,
            string startedAtUnixMs)
        {
            _frameSenderIdentityAddressUtf8 = addressUtf8;
            _frameSenderUuidBytes = senderUuidBytes;
            _frameStartedAtUnixMs = startedAtUnixMs;
        }

        private void ClearFrameSplitSenderIdentity()
        {
            _frameSenderIdentityAddressUtf8 = null;
            _frameSenderUuidBytes = null;
            _frameStartedAtUnixMs = null;
        }

        private void AddFrameSenderIdentityToSplitPacket(ulong timestamp)
        {
            if (_frameSenderIdentityAddressUtf8 == null)
            {
                return;
            }

            AddSenderIdentityMessage(
                timestamp,
                _frameSenderIdentityAddressUtf8,
                _frameSenderUuidBytes,
                _frameStartedAtUnixMs);
        }

        private int GetFrameSplitSenderIdentityElementSize()
        {
            if (_frameSenderIdentityAddressUtf8 == null)
            {
                return 0;
            }

            return 4 + GetSenderIdentityMessageSize(
                _frameSenderIdentityAddressUtf8.Length,
                _frameSenderUuidBytes.Length,
                _frameStartedAtUnixMs);
        }

        private int GetFloatMessageSize(int addressByteCount)
        {
            return GetOscStringSize(addressByteCount)
                + GetOscStringSize(1 + FloatMessageTypeCount)
                + 4;
        }

        private int GetSenderIdentityMessageSize(
            int addressByteCount,
            int senderUuidByteCount,
            string startedAtUnixMs)
        {
            return GetOscStringSize(addressByteCount)
                + GetOscStringSize(1 + SenderIdentityMessageTypeCount)
                + 4
                + GetAlignedSize(senderUuidByteCount)
                + GetOscStringSize(GetUtf8ByteCount(startedAtUnixMs));
        }

        private int GetStringMessageSize(int addressByteCount, ReadOnlySpan<string> values)
        {
            int size = GetOscStringSize(addressByteCount) + GetOscStringSize(1 + values.Length);
            for (int i = 0; i < values.Length; i++)
            {
                size += GetOscStringSize(GetUtf8ByteCount(values[i]));
            }

            return size;
        }

        private int GetPresetMessageSize(
            int addressByteCount,
            string presetName,
            bool includeCustomPrefix,
            string customPrefix)
        {
            int typeCount = includeCustomPrefix ? PresetCustomMessageTypeCount : PresetBaseMessageTypeCount;
            int size = GetOscStringSize(addressByteCount)
                + GetOscStringSize(1 + typeCount)
                + GetOscStringSize(GetUtf8ByteCount(presetName));

            if (includeCustomPrefix)
            {
                size += GetOscStringSize(GetUtf8ByteCount(customPrefix));
            }

            return size;
        }

        private static bool ShouldIncludeCustomPrefix(string presetName, string customPrefix)
        {
            return customPrefix != null &&
                string.Equals(presetName, PresetCustom, StringComparison.OrdinalIgnoreCase);
        }

        private static int GetOscStringSize(int utf8ByteCount)
        {
            return (utf8ByteCount + 4) & ~0x3;
        }

        private static int GetAlignedSize(int byteCount)
        {
            return (byteCount + 3) & ~0x3;
        }

        private static int GetUtf8ByteCount(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
        }

        private static void WriteOscString(byte[] buffer, ref int offset, byte[] utf8Bytes)
        {
            Buffer.BlockCopy(utf8Bytes, 0, buffer, offset, utf8Bytes.Length);
            offset += utf8Bytes.Length;
            WriteZeroPadding(buffer, ref offset, GetOscStringSize(utf8Bytes.Length) - utf8Bytes.Length);
        }

        private static void WriteOscString(byte[] buffer, ref int offset, string value)
        {
            int byteCount = GetUtf8ByteCount(value);
            if (byteCount > 0)
            {
                Encoding.UTF8.GetBytes(value, 0, value.Length, buffer, offset);
            }

            offset += byteCount;
            WriteZeroPadding(buffer, ref offset, GetOscStringSize(byteCount) - byteCount);
        }

        private static void WriteTypeTags(byte[] buffer, ref int offset, byte type, int count)
        {
            buffer[offset++] = (byte)',';
            for (int i = 0; i < count; i++)
            {
                buffer[offset++] = type;
            }

            WriteZeroPadding(buffer, ref offset, GetOscStringSize(1 + count) - (1 + count));
        }

        private static void WriteTypeTags(byte[] buffer, ref int offset, byte firstType, byte secondType)
        {
            buffer[offset++] = (byte)',';
            buffer[offset++] = firstType;
            buffer[offset++] = secondType;
            WriteZeroPadding(
                buffer,
                ref offset,
                GetOscStringSize(1 + SenderIdentityMessageTypeCount) - (1 + SenderIdentityMessageTypeCount));
        }

        private static void WriteBlob(byte[] buffer, ref int offset, byte[] value)
        {
            WriteInt32BigEndian(buffer, ref offset, value.Length);
            Buffer.BlockCopy(value, 0, buffer, offset, value.Length);
            offset += value.Length;
            WriteZeroPadding(buffer, ref offset, GetAlignedSize(value.Length) - value.Length);
        }

        private static void WriteZeroPadding(byte[] buffer, ref int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            offset += count;
        }

        private static void WriteInt32BigEndian(byte[] buffer, ref int offset, int value)
        {
            buffer[offset++] = (byte)((value >> 24) & 0xff);
            buffer[offset++] = (byte)((value >> 16) & 0xff);
            buffer[offset++] = (byte)((value >> 8) & 0xff);
            buffer[offset++] = (byte)(value & 0xff);
        }

        private static void WriteUInt64BigEndian(byte[] buffer, ref int offset, ulong value)
        {
            buffer[offset++] = (byte)((value >> 56) & 0xff);
            buffer[offset++] = (byte)((value >> 48) & 0xff);
            buffer[offset++] = (byte)((value >> 40) & 0xff);
            buffer[offset++] = (byte)((value >> 32) & 0xff);
            buffer[offset++] = (byte)((value >> 24) & 0xff);
            buffer[offset++] = (byte)((value >> 16) & 0xff);
            buffer[offset++] = (byte)((value >> 8) & 0xff);
            buffer[offset++] = (byte)(value & 0xff);
        }

        private static void WriteFloatBigEndian(byte[] buffer, ref int offset, float value)
        {
            var union = new FloatIntUnion { FloatValue = value };
            WriteInt32BigEndian(buffer, ref offset, union.IntValue);
        }

        private void EnsurePacketSlot(int index)
        {
            if (index < _buffers.Length)
            {
                return;
            }

            int newLength = _buffers.Length;
            while (newLength <= index)
            {
                newLength *= 2;
            }

            Array.Resize(ref _buffers, newLength);
            Array.Resize(ref _lengths, newLength);
            Array.Resize(ref _messageCounts, newLength);
            Array.Resize(ref _timestamps, newLength);
        }

        private void EnsurePacketBuffer(int index)
        {
            byte[] buffer = _buffers[index];
            if (buffer != null && buffer.Length >= _maxPacketSize)
            {
                return;
            }

            if (buffer != null)
            {
                _bytePool.Return(buffer);
            }

            _buffers[index] = _bytePool.Rent(_maxPacketSize);
        }

        private void LogMtuSplitIfNeeded()
        {
            if (!_splitInCurrentBuild || _hasLoggedMtuSplitWarning)
            {
                return;
            }

            _hasLoggedMtuSplitWarning = true;
            Debug.LogWarning(
                $"[OscBundleBuilder] OSC bundle exceeded MTU payload {_maxPacketSize} bytes and was split into {_packetCount} bundles with the same timestamp.");
        }

        private static void ValidateAddress(byte[] addressUtf8, string paramName)
        {
            if (addressUtf8 == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (addressUtf8.Length == 0 || addressUtf8[0] != (byte)'/')
            {
                throw new ArgumentException("OSC address must be a non-empty UTF-8 byte array starting with '/'.", paramName);
            }
        }

        private static void ValidatePresetName(string presetName, string paramName)
        {
            if (presetName == null)
            {
                throw new ArgumentNullException(paramName);
            }

            if (presetName.Length == 0)
            {
                throw new ArgumentException("Preset name must be a non-empty string.", paramName);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OscBundleBuilder));
            }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Explicit)]
        private struct FloatIntUnion
        {
            [System.Runtime.InteropServices.FieldOffset(0)]
            public float FloatValue;

            [System.Runtime.InteropServices.FieldOffset(0)]
            public int IntValue;
        }
    }
}
