using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;

namespace Hidano.FacialControl.Adapters.OSC
{
    /// <summary>
    /// 対応表の 1 チャンクが受け持つ項目の範囲。
    /// </summary>
    public readonly struct OscFrameLayoutChunk
    {
        public readonly int Start;
        public readonly int Count;

        public OscFrameLayoutChunk(int start, int count)
        {
            Start = start;
            Count = count;
        }
    }

    /// <summary>
    /// FacialControl 同士の値フレーム・対応表・対応表要求の OSC メッセージを読み書きする。
    /// <list type="bullet">
    /// <item><c>/_facialcontrol/values</c> <c>[i version, i offset, f value...]</c> — slot <c>offset</c> から始まる値の並び</item>
    /// <item><c>/_facialcontrol/layout</c> <c>[i version, i chunkIndex, i chunkCount, (i kind, s value)...]</c> — 対応表の 1 チャンク</item>
    /// <item><c>/_facialcontrol/layout_request</c> <c>[b senderUuid, i version, i chunkIndex...]</c> — chunkIndex を省くと全チャンク</item>
    /// </list>
    /// 値フレームの書き込み・読み取りはヒープ確保をしない。対応表と要求は頻度が低いので確保を許す。
    /// </summary>
    public static class OscIndexedFrameCodec
    {
        /// <summary>UDP 1 パケットに収める OSC メッセージの目安（バイト）。</summary>
        public const int DefaultMaxMessageBytes = 1400;

        private const byte TypeInt = (byte)'i';
        private const byte TypeFloat = (byte)'f';
        private const byte TypeString = (byte)'s';
        private const byte TypeBlob = (byte)'b';

        private const int ValuesHeaderArgumentCount = 2;
        private const int LayoutHeaderArgumentCount = 3;
        private const int RequestHeaderArgumentCount = 2;

        // ---- 値フレーム ----

        /// <summary><paramref name="valueCount"/> 個の値を載せた値フレームのバイト数。</summary>
        public static int GetValuesMessageSize(int valueCount)
        {
            if (valueCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(valueCount));
            }

            return GetPaddedStringSize(OscControlAddresses.ValuesUtf8.Length)
                + GetPaddedStringSize(1 + ValuesHeaderArgumentCount + valueCount)
                + (ValuesHeaderArgumentCount * 4)
                + (valueCount * 4);
        }

        /// <summary><paramref name="maxMessageBytes"/> に収まる値フレーム 1 通あたりの値の最大数。収まらなければ 0。</summary>
        public static int GetMaxValuesPerMessage(int maxMessageBytes)
        {
            if (maxMessageBytes < GetValuesMessageSize(1))
            {
                return 0;
            }

            // 値 1 個は値 4 バイト + 型タグ 1 バイト。型タグの 4 バイト境界で多少前後するので上から詰める。
            int count = (maxMessageBytes - GetValuesMessageSize(0)) / 5 + 4;
            while (count > 0 && GetValuesMessageSize(count) > maxMessageBytes)
            {
                count--;
            }

            return count;
        }

        /// <summary><paramref name="slotCount"/> 個の slot を送るのに要る値フレームの数（slot 0 個でも 1 通）。</summary>
        public static int GetValuesMessageCount(int slotCount, int maxValuesPerMessage)
        {
            if (slotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slotCount));
            }

            if (maxValuesPerMessage <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxValuesPerMessage));
            }

            if (slotCount == 0)
            {
                return 1;
            }

            return (slotCount + maxValuesPerMessage - 1) / maxValuesPerMessage;
        }

        /// <summary>
        /// 値フレームを <paramref name="destination"/> の先頭に書き込み、書いたバイト数を返す。ヒープ確保をしない。
        /// </summary>
        public static int WriteValuesMessage(
            Span<byte> destination,
            int version,
            int offset,
            ReadOnlySpan<float> values)
        {
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            int size = GetValuesMessageSize(values.Length);
            if (destination.Length < size)
            {
                throw new ArgumentException(
                    $"Destination is too small for the values message ({destination.Length} < {size} bytes).",
                    nameof(destination));
            }

            int position = 0;
            WritePaddedString(destination, ref position, OscControlAddresses.ValuesUtf8);

            int tagStart = position;
            destination[position++] = (byte)',';
            destination[position++] = TypeInt;
            destination[position++] = TypeInt;
            for (int i = 0; i < values.Length; i++)
            {
                destination[position++] = TypeFloat;
            }

            WriteTerminatorAndPadding(destination, ref position, tagStart);

            WriteInt32(destination, ref position, version);
            WriteInt32(destination, ref position, offset);
            for (int i = 0; i < values.Length; i++)
            {
                WriteInt32(destination, ref position, BitConverter.SingleToInt32Bits(values[i]));
            }

            return position;
        }

        public static bool IsValuesAddress(ReadOnlySpan<byte> address)
        {
            return address.SequenceEqual(OscControlAddresses.ValuesUtf8);
        }

        /// <summary>
        /// 値フレームのヘッダを読む。アドレスは確かめない（呼び出し側で <see cref="IsValuesAddress"/> を使う）。
        /// 型タグが <c>ii f...</c> でない、引数の長さが合わない、offset が負なら false。
        /// </summary>
        public static bool TryReadValuesHeader(
            in OscMessageView message,
            out int version,
            out int offset,
            out int valueCount)
        {
            version = OscFrameLayoutVersion.Unknown;
            offset = 0;
            valueCount = 0;

            ReadOnlySpan<byte> tags = message.TypeTags;
            if (tags.Length < ValuesHeaderArgumentCount || tags[0] != TypeInt || tags[1] != TypeInt)
            {
                return false;
            }

            for (int i = ValuesHeaderArgumentCount; i < tags.Length; i++)
            {
                if (tags[i] != TypeFloat)
                {
                    return false;
                }
            }

            int count = tags.Length - ValuesHeaderArgumentCount;
            ReadOnlySpan<byte> arguments = message.Arguments;
            if (arguments.Length != (ValuesHeaderArgumentCount + count) * 4)
            {
                return false;
            }

            int readOffset = BinaryPrimitives.ReadInt32BigEndian(arguments.Slice(4, 4));
            if (readOffset < 0)
            {
                return false;
            }

            version = BinaryPrimitives.ReadInt32BigEndian(arguments.Slice(0, 4));
            offset = readOffset;
            valueCount = count;
            return true;
        }

        /// <summary>
        /// 値フレームの値を <paramref name="slots"/> の offset 以降へ書き込む。ヒープ確保をしない。
        /// ヘッダが不正、または slot の範囲を超える場合は何も書かずに false。
        /// </summary>
        public static bool TryCopyValues(in OscMessageView message, Span<float> slots, out int writtenCount)
        {
            writtenCount = 0;
            if (!TryReadValuesHeader(in message, out _, out int offset, out int count))
            {
                return false;
            }

            if (offset > slots.Length || count > slots.Length - offset)
            {
                return false;
            }

            ReadOnlySpan<byte> arguments = message.Arguments;
            int position = ValuesHeaderArgumentCount * 4;
            for (int i = 0; i < count; i++)
            {
                slots[offset + i] = BitConverter.Int32BitsToSingle(
                    BinaryPrimitives.ReadInt32BigEndian(arguments.Slice(position, 4)));
                position += 4;
            }

            writtenCount = count;
            return true;
        }

        // ---- 対応表 ----

        /// <summary>項目 <paramref name="start"/> から <paramref name="count"/> 個を載せた対応表チャンクのバイト数。</summary>
        public static int GetLayoutMessageSize(IReadOnlyList<OscFrameLayoutEntry> entries, int start, int count)
        {
            ValidateRange(entries, start, count);

            int size = GetPaddedStringSize(OscControlAddresses.LayoutUtf8.Length)
                + GetPaddedStringSize(1 + LayoutHeaderArgumentCount + (count * 2))
                + (LayoutHeaderArgumentCount * 4);
            for (int i = start; i < start + count; i++)
            {
                size += GetLayoutEntryArgumentsSize(entries[i]);
            }

            return size;
        }

        /// <summary>
        /// 対応表の項目を、1 チャンクが <paramref name="maxMessageBytes"/> に収まるように前から分ける。
        /// 項目 1 つだけで上限を超える場合は、その項目だけのチャンクにする（上限を超えたまま送る）。
        /// 項目が 0 個でもチャンクは 1 つ返す（受信側が「空の対応表が揃った」と判断できるように）。
        /// </summary>
        public static OscFrameLayoutChunk[] SplitLayout(IReadOnlyList<OscFrameLayoutEntry> entries, int maxMessageBytes)
        {
            int entryCount = entries != null ? entries.Count : 0;
            if (entryCount == 0)
            {
                return new[] { new OscFrameLayoutChunk(0, 0) };
            }

            int fixedSize = GetPaddedStringSize(OscControlAddresses.LayoutUtf8.Length)
                + (LayoutHeaderArgumentCount * 4);
            var chunks = new List<OscFrameLayoutChunk>();
            int start = 0;
            int count = 0;
            int argumentsSize = 0;
            for (int i = 0; i < entryCount; i++)
            {
                int entrySize = GetLayoutEntryArgumentsSize(entries[i]);
                int sizeWithEntry = fixedSize
                    + GetPaddedStringSize(1 + LayoutHeaderArgumentCount + ((count + 1) * 2))
                    + argumentsSize
                    + entrySize;
                if (count > 0 && sizeWithEntry > maxMessageBytes)
                {
                    chunks.Add(new OscFrameLayoutChunk(start, count));
                    start = i;
                    count = 0;
                    argumentsSize = 0;
                }

                count++;
                argumentsSize += entrySize;
            }

            chunks.Add(new OscFrameLayoutChunk(start, count));
            return chunks.ToArray();
        }

        /// <summary>対応表の 1 チャンクを OSC メッセージにする。</summary>
        public static byte[] WriteLayoutMessage(
            int version,
            int chunkIndex,
            int chunkCount,
            IReadOnlyList<OscFrameLayoutEntry> entries,
            OscFrameLayoutChunk chunk)
        {
            if (chunkCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkCount));
            }

            if (chunkIndex < 0 || chunkIndex >= chunkCount)
            {
                throw new ArgumentOutOfRangeException(nameof(chunkIndex));
            }

            int size = GetLayoutMessageSize(entries, chunk.Start, chunk.Count);
            var buffer = new byte[size];
            var destination = new Span<byte>(buffer);
            int position = 0;
            WritePaddedString(destination, ref position, OscControlAddresses.LayoutUtf8);

            int tagStart = position;
            destination[position++] = (byte)',';
            destination[position++] = TypeInt;
            destination[position++] = TypeInt;
            destination[position++] = TypeInt;
            for (int i = 0; i < chunk.Count; i++)
            {
                destination[position++] = TypeInt;
                destination[position++] = TypeString;
            }

            WriteTerminatorAndPadding(destination, ref position, tagStart);

            WriteInt32(destination, ref position, version);
            WriteInt32(destination, ref position, chunkIndex);
            WriteInt32(destination, ref position, chunkCount);
            for (int i = chunk.Start; i < chunk.Start + chunk.Count; i++)
            {
                OscFrameLayoutEntry entry = entries[i];
                WriteInt32(destination, ref position, (int)entry.Kind);
                WritePaddedString(destination, ref position, Encoding.UTF8.GetBytes(entry.Value ?? string.Empty));
            }

            return buffer;
        }

        public static bool IsLayoutAddress(ReadOnlySpan<byte> address)
        {
            return address.SequenceEqual(OscControlAddresses.LayoutUtf8);
        }

        /// <summary>
        /// 対応表の 1 チャンクを読む。<paramref name="entries"/> はクリアしてから項目を追加する。
        /// 型タグ・引数・チャンク番号・項目の種類のいずれかが不正なら false（<paramref name="entries"/> は空）。
        /// </summary>
        public static bool TryReadLayoutMessage(
            in OscMessageView message,
            out int version,
            out int chunkIndex,
            out int chunkCount,
            List<OscFrameLayoutEntry> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            entries.Clear();
            version = OscFrameLayoutVersion.Unknown;
            chunkIndex = 0;
            chunkCount = 0;

            ReadOnlySpan<byte> tags = message.TypeTags;
            if (tags.Length < LayoutHeaderArgumentCount
                || ((tags.Length - LayoutHeaderArgumentCount) % 2) != 0)
            {
                return false;
            }

            OscArgumentReader reader = message.GetArgumentReader();
            if (!TryReadInt(ref reader, out int readVersion)
                || !TryReadInt(ref reader, out int readChunkIndex)
                || !TryReadInt(ref reader, out int readChunkCount))
            {
                return false;
            }

            if (readChunkCount <= 0 || readChunkIndex < 0 || readChunkIndex >= readChunkCount)
            {
                return false;
            }

            int entryCount = (tags.Length - LayoutHeaderArgumentCount) / 2;
            for (int i = 0; i < entryCount; i++)
            {
                if (!TryReadInt(ref reader, out int kind)
                    || !OscFrameLayoutEntry.IsDefinedKind(kind)
                    || !reader.TryReadNext(out OscArgument value)
                    || !value.IsString)
                {
                    entries.Clear();
                    return false;
                }

                entries.Add(new OscFrameLayoutEntry((OscFrameLayoutEntryKind)kind, Encoding.UTF8.GetString(value.Bytes)));
            }

            if (!reader.IsFullyConsumed)
            {
                entries.Clear();
                return false;
            }

            version = readVersion;
            chunkIndex = readChunkIndex;
            chunkCount = readChunkCount;
            return true;
        }

        // ---- 対応表要求 ----

        /// <summary>
        /// 対応表要求を OSC メッセージにする。<paramref name="chunkIndices"/> が null か空なら全チャンクの要求。
        /// </summary>
        public static byte[] WriteLayoutRequestMessage(Guid senderUuid, int version, IReadOnlyList<int> chunkIndices)
        {
            int indexCount = chunkIndices != null ? chunkIndices.Count : 0;
            int size = GetPaddedStringSize(OscControlAddresses.LayoutRequestUtf8.Length)
                + GetPaddedStringSize(1 + RequestHeaderArgumentCount + indexCount)
                + 4 + SenderIdentity.UuidByteLength
                + 4
                + (indexCount * 4);
            var buffer = new byte[size];
            var destination = new Span<byte>(buffer);
            int position = 0;
            WritePaddedString(destination, ref position, OscControlAddresses.LayoutRequestUtf8);

            int tagStart = position;
            destination[position++] = (byte)',';
            destination[position++] = TypeBlob;
            destination[position++] = TypeInt;
            for (int i = 0; i < indexCount; i++)
            {
                destination[position++] = TypeInt;
            }

            WriteTerminatorAndPadding(destination, ref position, tagStart);

            WriteInt32(destination, ref position, SenderIdentity.UuidByteLength);
            if (!senderUuid.TryWriteBytes(destination.Slice(position, SenderIdentity.UuidByteLength)))
            {
                throw new InvalidOperationException("Failed to write the sender UUID.");
            }

            position += SenderIdentity.UuidByteLength;
            WriteInt32(destination, ref position, version);
            for (int i = 0; i < indexCount; i++)
            {
                WriteInt32(destination, ref position, chunkIndices[i]);
            }

            return buffer;
        }

        public static bool IsLayoutRequestAddress(ReadOnlySpan<byte> address)
        {
            return address.SequenceEqual(OscControlAddresses.LayoutRequestUtf8);
        }

        /// <summary>
        /// 対応表要求を読む。<paramref name="chunkIndices"/> はクリアしてから追加し、空なら全チャンクの要求。
        /// 負のチャンク番号を含む等、不正なら false。
        /// </summary>
        public static bool TryReadLayoutRequestMessage(
            in OscMessageView message,
            out Guid senderUuid,
            out int version,
            List<int> chunkIndices)
        {
            if (chunkIndices == null)
            {
                throw new ArgumentNullException(nameof(chunkIndices));
            }

            chunkIndices.Clear();
            senderUuid = Guid.Empty;
            version = OscFrameLayoutVersion.Unknown;

            OscArgumentReader reader = message.GetArgumentReader();
            if (!reader.TryReadNext(out OscArgument uuidArgument)
                || !uuidArgument.IsBlob
                || uuidArgument.Bytes.Length != SenderIdentity.UuidByteLength)
            {
                return false;
            }

            var uuid = new Guid(uuidArgument.Bytes);
            if (!TryReadInt(ref reader, out int readVersion))
            {
                return false;
            }

            int indexCount = message.TypeTags.Length - RequestHeaderArgumentCount;
            for (int i = 0; i < indexCount; i++)
            {
                if (!TryReadInt(ref reader, out int chunkIndex) || chunkIndex < 0)
                {
                    chunkIndices.Clear();
                    return false;
                }

                chunkIndices.Add(chunkIndex);
            }

            if (!reader.IsFullyConsumed)
            {
                chunkIndices.Clear();
                return false;
            }

            senderUuid = uuid;
            version = readVersion;
            return true;
        }

        // ---- 共通 ----

        private static bool TryReadInt(ref OscArgumentReader reader, out int value)
        {
            if (!reader.TryReadNext(out OscArgument argument))
            {
                value = default;
                return false;
            }

            return argument.TryGetInt32(out value);
        }

        private static int GetLayoutEntryArgumentsSize(OscFrameLayoutEntry entry)
        {
            return 4 + GetPaddedStringSize(Encoding.UTF8.GetByteCount(entry.Value ?? string.Empty));
        }

        private static void ValidateRange(IReadOnlyList<OscFrameLayoutEntry> entries, int start, int count)
        {
            int entryCount = entries != null ? entries.Count : 0;
            if (start < 0 || count < 0 || start > entryCount - count)
            {
                throw new ArgumentOutOfRangeException(nameof(count), "The layout entry range is outside the list bounds.");
            }
        }

        /// <summary>NUL 終端込みで 4 バイト境界に揃えた OSC 文字列のバイト数。</summary>
        private static int GetPaddedStringSize(int byteCount)
        {
            return (byteCount + 4) & ~3;
        }

        private static void WritePaddedString(Span<byte> destination, ref int position, ReadOnlySpan<byte> utf8)
        {
            int start = position;
            utf8.CopyTo(destination.Slice(position));
            position += utf8.Length;
            WriteTerminatorAndPadding(destination, ref position, start);
        }

        /// <summary>NUL を 1 バイト以上書き、<paramref name="start"/> からの長さを 4 の倍数に揃える。</summary>
        private static void WriteTerminatorAndPadding(Span<byte> destination, ref int position, int start)
        {
            destination[position++] = 0;
            while (((position - start) & 3) != 0)
            {
                destination[position++] = 0;
            }
        }

        private static void WriteInt32(Span<byte> destination, ref int position, int value)
        {
            BinaryPrimitives.WriteInt32BigEndian(destination.Slice(position, 4), value);
            position += 4;
        }
    }
}
