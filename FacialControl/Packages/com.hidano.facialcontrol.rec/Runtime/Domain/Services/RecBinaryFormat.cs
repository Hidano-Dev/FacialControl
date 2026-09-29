using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Hidano.FacialControl.Rec.Domain.Models;

namespace Hidano.FacialControl.Rec.Domain.Services
{
    /// <summary>
    /// Reads and writes the REC sidecar binary container.
    /// </summary>
    public static class RecBinaryFormat
    {
        public const ushort CurrentFormatVersion = 1;
        // flags は予約（常に 0）。読み込み側は値を検証しないため、将来ビットを割り当てても
        // （例: 開始時刻の出自 = 壁時計 / 外部タイムコード）formatVersion を上げずに済む。
        public const ushort DefaultFlags = 0;
        public const int HeaderSize = 16;
        public const int FooterRecordSize = 13;

        private const byte MagicF = (byte)'F';
        private const byte MagicR = (byte)'R';
        private const byte MagicE = (byte)'E';
        private const byte MagicC = (byte)'C';

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public readonly struct Header
        {
            public Header(ushort formatVersion, ushort flags, long startedAtUnixMilliseconds)
            {
                FormatVersion = formatVersion;
                Flags = flags;
                StartedAtUnixMilliseconds = startedAtUnixMilliseconds;
            }

            public ushort FormatVersion { get; }

            public ushort Flags { get; }

            public long StartedAtUnixMilliseconds { get; }
        }

        public sealed class ReadResult
        {
            public ReadResult(
                Header header,
                RecTimeline timeline,
                bool hasFooter,
                bool recoveredFromTruncatedTail,
                uint recordCount)
            {
                Header = header;
                Timeline = timeline ?? throw new ArgumentNullException(nameof(timeline));
                HasFooter = hasFooter;
                RecoveredFromTruncatedTail = recoveredFromTruncatedTail;
                RecordCount = recordCount;
            }

            public Header Header { get; }

            public RecTimeline Timeline { get; }

            public bool HasFooter { get; }

            public bool RecoveredFromTruncatedTail { get; }

            public uint RecordCount { get; }
        }

        public static int GetMaxRecordSize(int maxIdUtf8ByteCount, int maxAxisCount)
        {
            if (maxIdUtf8ByteCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxIdUtf8ByteCount));
            }

            if (maxAxisCount < 0 || maxAxisCount > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(maxAxisCount));
            }

            int idDefineSize = 1 + 2 + 1 + 2 + maxIdUtf8ByteCount;
            int analogSize = 1 + 8 + 2 + 1 + (4 * maxAxisCount);
            return Math.Max(idDefineSize, analogSize);
        }

        public static int GetSerializedSize(RecTimeline timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            int size = HeaderSize + FooterRecordSize;

            for (int i = 0; i < timeline.SourceIds.Count; i++)
            {
                size += GetIdDefineRecordSize(timeline.SourceIds[i]);
            }

            for (int i = 0; i < timeline.ExpressionIds.Count; i++)
            {
                size += GetIdDefineRecordSize(timeline.ExpressionIds[i]);
            }

            foreach (RecBaselineState.TriggerEntry entry in timeline.Baseline.TriggerEntries)
            {
                size += 5 * entry.ExpressionIds.Count;
            }

            foreach (RecBaselineState.AnalogEntry entry in timeline.Baseline.AnalogEntries)
            {
                size += 4 + (4 * entry.Axes.Count);
            }

            for (int i = 0; i < timeline.Events.Count; i++)
            {
                size += GetEventRecordSize(timeline.Events[i], timeline.GetAnalogAxes(i).Count);
            }

            return size;
        }

        public static byte[] Serialize(RecTimeline timeline, long startedAtUnixMilliseconds)
        {
            byte[] bytes = new byte[GetSerializedSize(timeline)];
            int written = Write(timeline, startedAtUnixMilliseconds, bytes);
            if (written != bytes.Length)
            {
                throw new InvalidOperationException("Serialized REC size did not match the expected size.");
            }

            return bytes;
        }

        public static int Write(RecTimeline timeline, long startedAtUnixMilliseconds, Span<byte> destination)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            int requiredSize = GetSerializedSize(timeline);
            if (destination.Length < requiredSize)
            {
                throw new ArgumentException("Destination buffer is too small.", nameof(destination));
            }

            int offset = 0;
            offset += WriteHeader(destination, startedAtUnixMilliseconds);

            var idTable = new RecIdTable();
            for (int i = 0; i < timeline.SourceIds.Count; i++)
            {
                offset += WriteRecord(
                    destination.Slice(offset),
                    RecEvent.CreateIdDefine(idTable.GetOrAddSourceId(timeline.SourceIds[i]), RecEvent.IdDefinitionKind.Source),
                    ReadOnlySpan<float>.Empty,
                    timeline.SourceIds[i]);
            }

            for (int i = 0; i < timeline.ExpressionIds.Count; i++)
            {
                offset += WriteRecord(
                    destination.Slice(offset),
                    RecEvent.CreateIdDefine(idTable.GetOrAddExpressionId(timeline.ExpressionIds[i]), RecEvent.IdDefinitionKind.Expression),
                    ReadOnlySpan<float>.Empty,
                    timeline.ExpressionIds[i]);
            }

            uint recordCount = checked((uint)(timeline.SourceIds.Count + timeline.ExpressionIds.Count));

            for (int i = 0; i < timeline.Baseline.TriggerEntries.Count; i++)
            {
                RecBaselineState.TriggerEntry entry = timeline.Baseline.TriggerEntries[i];
                if (!idTable.TryGetSourceIndex(entry.SourceId, out ushort sourceIndex))
                {
                    throw new InvalidOperationException("Unknown baseline trigger source id.");
                }

                for (int j = 0; j < entry.ExpressionIds.Count; j++)
                {
                    if (!idTable.TryGetExpressionIndex(entry.ExpressionIds[j], out ushort expressionIndex))
                    {
                        throw new InvalidOperationException("Unknown baseline trigger expression id.");
                    }

                    offset += WriteRecord(destination.Slice(offset), RecEvent.CreateBaselineTrigger(sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty);
                    recordCount++;
                }
            }

            for (int i = 0; i < timeline.Baseline.AnalogEntries.Count; i++)
            {
                RecBaselineState.AnalogEntry entry = timeline.Baseline.AnalogEntries[i];
                if (!idTable.TryGetSourceIndex(entry.SourceId, out ushort sourceIndex))
                {
                    throw new InvalidOperationException("Unknown baseline analog source id.");
                }

                float[] axes = entry.Axes.ToArray();
                offset += WriteRecord(destination.Slice(offset), RecEvent.CreateBaselineAnalog(sourceIndex, checked((byte)axes.Length)), axes);
                recordCount++;
            }

            for (int i = 0; i < timeline.Events.Count; i++)
            {
                IReadOnlyList<float> axes = timeline.GetAnalogAxes(i);
                offset += WriteNonIdRecord(destination.Slice(offset), timeline.Events[i], CopyAxesToTemp(axes));
                recordCount++;
            }

            offset += WriteFooter(destination.Slice(offset), timeline.DurationSeconds, recordCount);
            return offset;
        }

        public static bool TryRead(ReadOnlySpan<byte> source, out ReadResult result, out string error)
        {
            result = null;
            error = null;

            if (source.Length < HeaderSize)
            {
                error = "REC file is shorter than the required header.";
                return false;
            }

            if (source[0] != MagicF || source[1] != MagicR || source[2] != MagicE || source[3] != MagicC)
            {
                error = "REC file magic did not match 'FREC'.";
                return false;
            }

            ushort formatVersion = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2));
            if (formatVersion != CurrentFormatVersion)
            {
                error = $"Unsupported REC format version {formatVersion}. Expected {CurrentFormatVersion}.";
                return false;
            }

            var header = new Header(
                formatVersion,
                BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(6, 2)),
                BinaryPrimitives.ReadInt64LittleEndian(source.Slice(8, 8)));

            int offset = HeaderSize;
            bool hasFooter = false;
            bool recoveredFromTruncatedTail = false;
            double durationSeconds = 0d;
            uint footerRecordCount = 0;
            uint parsedRecordCount = 0;

            var idTable = new RecIdTable();
            var baselineTriggerRecords = new List<(ushort sourceIndex, ushort expressionIndex)>();
            var baselineAnalogRecords = new List<(ushort sourceIndex, float[] axes)>();
            var events = new List<RecEvent>();
            var analogAxesByEvent = new List<IReadOnlyList<float>>();

            while (offset < source.Length)
            {
                byte kindValue = source[offset];
                RecEventKind kind = (RecEventKind)kindValue;

                if (!TryReadRecord(
                    source.Slice(offset),
                    kind,
                    idTable,
                    baselineTriggerRecords,
                    baselineAnalogRecords,
                    events,
                    analogAxesByEvent,
                    ref parsedRecordCount,
                    ref hasFooter,
                    ref durationSeconds,
                    ref footerRecordCount,
                    out int recordBytes,
                    out error))
                {
                    if (error != null)
                    {
                        return false;
                    }

                    recoveredFromTruncatedTail = true;
                    break;
                }

                offset += recordBytes;
                if (hasFooter)
                {
                    break;
                }
            }

            if (hasFooter && footerRecordCount != parsedRecordCount)
            {
                error = $"Footer event count {footerRecordCount} did not match parsed record count {parsedRecordCount}.";
                return false;
            }

            if (!hasFooter)
            {
                recoveredFromTruncatedTail = true;
                if (events.Count > 0)
                {
                    durationSeconds = events[events.Count - 1].TimestampSeconds;
                }
            }

            if (!TryBuildTimeline(idTable, baselineTriggerRecords, baselineAnalogRecords, events, analogAxesByEvent, durationSeconds, out RecTimeline timeline, out error))
            {
                return false;
            }

            result = new ReadResult(header, timeline, hasFooter, recoveredFromTruncatedTail, parsedRecordCount);
            return true;
        }

        public static int WriteHeader(Span<byte> destination, long startedAtUnixMilliseconds)
        {
            if (destination.Length < HeaderSize)
            {
                throw new ArgumentException("Destination buffer is too small for the REC header.", nameof(destination));
            }

            destination[0] = MagicF;
            destination[1] = MagicR;
            destination[2] = MagicE;
            destination[3] = MagicC;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), CurrentFormatVersion);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(6, 2), DefaultFlags);
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(8, 8), startedAtUnixMilliseconds);
            return HeaderSize;
        }

        public static int WriteRecord(Span<byte> destination, in RecEvent evt, ReadOnlySpan<float> axes, string idValue = null)
        {
            if (evt.Kind == RecEventKind.IdDefine)
            {
                if (string.IsNullOrWhiteSpace(idValue))
                {
                    throw new ArgumentException("IdDefine records require an id value.", nameof(idValue));
                }

                if (!axes.IsEmpty)
                {
                    throw new ArgumentException("IdDefine records do not accept axis payloads.", nameof(axes));
                }

                return WriteIdDefine(destination, evt.IdIndex, evt.DefinedIdKind, idValue);
            }

            return WriteNonIdRecord(destination, evt, axes);
        }

        private static int GetIdDefineRecordSize(string value)
        {
            return 1 + 2 + 1 + 2 + StrictUtf8.GetByteCount(value);
        }

        private static int GetEventRecordSize(RecEvent evt, int axesCount)
        {
            switch (evt.Kind)
            {
                case RecEventKind.TriggerOn:
                case RecEventKind.TriggerOff:
                    return 1 + 8 + 2 + 2;
                case RecEventKind.AnalogSample:
                    return 1 + 8 + 2 + 1 + (4 * axesCount);
                case RecEventKind.BaselineTrigger:
                    return 1 + 2 + 2;
                case RecEventKind.BaselineAnalog:
                    return 1 + 2 + 1 + (4 * axesCount);
                default:
                    throw new ArgumentOutOfRangeException(nameof(evt), $"Unsupported event kind {evt.Kind}.");
            }
        }

        private static int WriteIdDefine(Span<byte> destination, ushort idIndex, RecEvent.IdDefinitionKind idKind, string value)
        {
            int utf8ByteCount = StrictUtf8.GetByteCount(value);
            int recordSize = 1 + 2 + 1 + 2 + utf8ByteCount;
            if (destination.Length < recordSize)
            {
                throw new ArgumentException("Destination buffer is too small for the IdDefine record.", nameof(destination));
            }

            destination[0] = (byte)RecEventKind.IdDefine;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), idIndex);
            destination[3] = (byte)idKind;
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(4, 2), checked((ushort)utf8ByteCount));
            StrictUtf8.GetBytes(value.AsSpan(), destination.Slice(6, utf8ByteCount));
            return recordSize;
        }

        private static int WriteNonIdRecord(Span<byte> destination, RecEvent evt, ReadOnlySpan<float> axes)
        {
            int recordSize = GetEventRecordSize(evt, axes.Length);
            if (destination.Length < recordSize)
            {
                throw new ArgumentException("Destination buffer is too small for the REC record.", nameof(destination));
            }

            destination[0] = (byte)evt.Kind;
            switch (evt.Kind)
            {
                case RecEventKind.TriggerOn:
                case RecEventKind.TriggerOff:
                    WriteTimedTrigger(destination.Slice(1), evt);
                    break;
                case RecEventKind.AnalogSample:
                    WriteTimedAnalog(destination.Slice(1), evt, axes);
                    break;
                case RecEventKind.BaselineTrigger:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), evt.SourceIdIndex);
                    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(3, 2), evt.ExpressionIdIndex);
                    break;
                case RecEventKind.BaselineAnalog:
                    BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(1, 2), evt.SourceIdIndex);
                    destination[3] = evt.AxisCount;
                    WriteAxes(destination.Slice(4), axes);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(evt), $"Unsupported event kind {evt.Kind}.");
            }

            return recordSize;
        }

        public static int WriteFooter(Span<byte> destination, double durationSeconds, uint recordCount)
        {
            if (destination.Length < FooterRecordSize)
            {
                throw new ArgumentException("Destination buffer is too small for the REC footer.", nameof(destination));
            }

            destination[0] = (byte)RecEventKind.Footer;
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(1, 8), BitConverter.DoubleToInt64Bits(durationSeconds));
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(9, 4), recordCount);
            return FooterRecordSize;
        }

        private static void WriteTimedTrigger(Span<byte> destination, RecEvent evt)
        {
            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(0, 8), BitConverter.DoubleToInt64Bits(evt.TimestampSeconds));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), evt.SourceIdIndex);
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(10, 2), evt.ExpressionIdIndex);
        }

        private static void WriteTimedAnalog(Span<byte> destination, RecEvent evt, ReadOnlySpan<float> axes)
        {
            if (axes.Length != evt.AxisCount)
            {
                throw new ArgumentException("Axis payload length must match the event axis count.", nameof(axes));
            }

            BinaryPrimitives.WriteInt64LittleEndian(destination.Slice(0, 8), BitConverter.DoubleToInt64Bits(evt.TimestampSeconds));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(8, 2), evt.SourceIdIndex);
            destination[10] = evt.AxisCount;
            WriteAxes(destination.Slice(11), axes);
        }

        private static void WriteAxes(Span<byte> destination, ReadOnlySpan<float> axes)
        {
            for (int i = 0; i < axes.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(destination.Slice(i * 4, 4), BitConverter.SingleToInt32Bits(axes[i]));
            }
        }

        private static bool TryReadRecord(
            ReadOnlySpan<byte> source,
            RecEventKind kind,
            RecIdTable idTable,
            List<(ushort sourceIndex, ushort expressionIndex)> baselineTriggerRecords,
            List<(ushort sourceIndex, float[] axes)> baselineAnalogRecords,
            List<RecEvent> events,
            List<IReadOnlyList<float>> analogAxesByEvent,
            ref uint parsedRecordCount,
            ref bool hasFooter,
            ref double durationSeconds,
            ref uint footerRecordCount,
            out int recordBytes,
            out string error)
        {
            recordBytes = 0;
            error = null;

            switch (kind)
            {
                case RecEventKind.IdDefine:
                    return TryReadIdDefine(source, idTable, ref parsedRecordCount, out recordBytes, out error);
                case RecEventKind.TriggerOn:
                case RecEventKind.TriggerOff:
                    return TryReadTimedTrigger(source, kind, events, analogAxesByEvent, ref parsedRecordCount, out recordBytes);
                case RecEventKind.AnalogSample:
                    return TryReadTimedAnalog(source, events, analogAxesByEvent, ref parsedRecordCount, out recordBytes);
                case RecEventKind.BaselineTrigger:
                    return TryReadBaselineTrigger(source, baselineTriggerRecords, ref parsedRecordCount, out recordBytes);
                case RecEventKind.BaselineAnalog:
                    return TryReadBaselineAnalog(source, baselineAnalogRecords, ref parsedRecordCount, out recordBytes);
                case RecEventKind.Footer:
                    return TryReadFooter(source, ref hasFooter, ref durationSeconds, ref footerRecordCount, out recordBytes);
                default:
                    error = $"Unknown REC record kind {source[0]}.";
                    return false;
            }
        }

        private static bool TryReadIdDefine(
            ReadOnlySpan<byte> source,
            RecIdTable idTable,
            ref uint parsedRecordCount,
            out int recordBytes,
            out string error)
        {
            recordBytes = 0;
            error = null;
            if (source.Length < 6)
            {
                return false;
            }

            ushort idIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(1, 2));
            RecEvent.IdDefinitionKind idKind = (RecEvent.IdDefinitionKind)source[3];
            ushort utf8Length = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(4, 2));
            int payloadSize = 6 + utf8Length;
            if (source.Length < payloadSize)
            {
                return false;
            }

            try
            {
                string value = StrictUtf8.GetString(source.Slice(6, utf8Length));
                idTable.AddDefinedId(idIndex, idKind, value);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is ArgumentOutOfRangeException || ex is DecoderFallbackException || ex is InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }

            parsedRecordCount++;
            recordBytes = payloadSize;
            return true;
        }

        private static bool TryReadTimedTrigger(
            ReadOnlySpan<byte> source,
            RecEventKind kind,
            List<RecEvent> events,
            List<IReadOnlyList<float>> analogAxesByEvent,
            ref uint parsedRecordCount,
            out int recordBytes)
        {
            recordBytes = 0;
            if (source.Length < 13)
            {
                return false;
            }

            double timestampSeconds = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(source.Slice(1, 8)));
            ushort sourceIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(9, 2));
            ushort expressionIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(11, 2));
            events.Add(kind == RecEventKind.TriggerOn
                ? RecEvent.CreateTriggerOn(timestampSeconds, sourceIndex, expressionIndex)
                : RecEvent.CreateTriggerOff(timestampSeconds, sourceIndex, expressionIndex));
            analogAxesByEvent.Add(Array.Empty<float>());
            parsedRecordCount++;
            recordBytes = 13;
            return true;
        }

        private static bool TryReadTimedAnalog(
            ReadOnlySpan<byte> source,
            List<RecEvent> events,
            List<IReadOnlyList<float>> analogAxesByEvent,
            ref uint parsedRecordCount,
            out int recordBytes)
        {
            recordBytes = 0;
            if (source.Length < 12)
            {
                return false;
            }

            double timestampSeconds = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(source.Slice(1, 8)));
            ushort sourceIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(9, 2));
            byte axisCount = source[11];
            int payloadSize = 12 + (axisCount * 4);
            if (source.Length < payloadSize)
            {
                return false;
            }

            float[] axes = ReadAxes(source.Slice(12, axisCount * 4), axisCount);
            events.Add(RecEvent.CreateAnalogSample(timestampSeconds, sourceIndex, axisCount));
            analogAxesByEvent.Add(axes);
            parsedRecordCount++;
            recordBytes = payloadSize;
            return true;
        }

        private static bool TryReadBaselineTrigger(
            ReadOnlySpan<byte> source,
            List<(ushort sourceIndex, ushort expressionIndex)> baselineTriggerRecords,
            ref uint parsedRecordCount,
            out int recordBytes)
        {
            recordBytes = 0;
            if (source.Length < 5)
            {
                return false;
            }

            baselineTriggerRecords.Add((
                BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(1, 2)),
                BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(3, 2))));
            parsedRecordCount++;
            recordBytes = 5;
            return true;
        }

        private static bool TryReadBaselineAnalog(
            ReadOnlySpan<byte> source,
            List<(ushort sourceIndex, float[] axes)> baselineAnalogRecords,
            ref uint parsedRecordCount,
            out int recordBytes)
        {
            recordBytes = 0;
            if (source.Length < 4)
            {
                return false;
            }

            ushort sourceIndex = BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(1, 2));
            byte axisCount = source[3];
            int payloadSize = 4 + (axisCount * 4);
            if (source.Length < payloadSize)
            {
                return false;
            }

            baselineAnalogRecords.Add((sourceIndex, ReadAxes(source.Slice(4, axisCount * 4), axisCount)));
            parsedRecordCount++;
            recordBytes = payloadSize;
            return true;
        }

        private static bool TryReadFooter(
            ReadOnlySpan<byte> source,
            ref bool hasFooter,
            ref double durationSeconds,
            ref uint footerRecordCount,
            out int recordBytes)
        {
            recordBytes = 0;
            if (source.Length < FooterRecordSize)
            {
                return false;
            }

            durationSeconds = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64LittleEndian(source.Slice(1, 8)));
            footerRecordCount = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(9, 4));
            hasFooter = true;
            recordBytes = FooterRecordSize;
            return true;
        }

        private static float[] ReadAxes(ReadOnlySpan<byte> source, int axisCount)
        {
            var axes = new float[axisCount];
            for (int i = 0; i < axisCount; i++)
            {
                axes[i] = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(source.Slice(i * 4, 4)));
            }

            return axes;
        }

        private static bool TryBuildTimeline(
            RecIdTable idTable,
            List<(ushort sourceIndex, ushort expressionIndex)> baselineTriggerRecords,
            List<(ushort sourceIndex, float[] axes)> baselineAnalogRecords,
            List<RecEvent> events,
            List<IReadOnlyList<float>> analogAxesByEvent,
            double durationSeconds,
            out RecTimeline timeline,
            out string error)
        {
            timeline = null;
            error = null;

            string[] sourceIds = idTable.SourceIds.ToArray();
            string[] expressionIds = idTable.ExpressionIds.ToArray();

            try
            {
                var triggerEntries = BuildBaselineTriggerEntries(idTable, baselineTriggerRecords);
                var analogEntries = BuildBaselineAnalogEntries(idTable, baselineAnalogRecords);
                var baseline = new RecBaselineState(triggerEntries, analogEntries);
                timeline = new RecTimeline(
                    baseline,
                    events,
                    sourceIds,
                    expressionIds,
                    durationSeconds,
                    analogAxesByEvent);
                return true;
            }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
            {
                error = ex.Message;
                return false;
            }
        }

        private static IReadOnlyList<RecBaselineState.TriggerEntry> BuildBaselineTriggerEntries(
            RecIdTable idTable,
            List<(ushort sourceIndex, ushort expressionIndex)> baselineTriggerRecords)
        {
            var orderedSources = new List<string>();
            var perSource = new Dictionary<string, List<string>>(StringComparer.Ordinal);

            for (int i = 0; i < baselineTriggerRecords.Count; i++)
            {
                if (!idTable.TryGetSourceId(baselineTriggerRecords[i].sourceIndex, out string sourceId))
                {
                    throw new InvalidOperationException($"Baseline trigger referenced undefined source index {baselineTriggerRecords[i].sourceIndex}.");
                }

                if (!idTable.TryGetExpressionId(baselineTriggerRecords[i].expressionIndex, out string expressionId))
                {
                    throw new InvalidOperationException($"Baseline trigger referenced undefined expression index {baselineTriggerRecords[i].expressionIndex}.");
                }

                if (!perSource.TryGetValue(sourceId, out List<string> expressions))
                {
                    expressions = new List<string>();
                    perSource.Add(sourceId, expressions);
                    orderedSources.Add(sourceId);
                }

                expressions.Add(expressionId);
            }

            var entries = new List<RecBaselineState.TriggerEntry>(orderedSources.Count);
            for (int i = 0; i < orderedSources.Count; i++)
            {
                string sourceId = orderedSources[i];
                entries.Add(new RecBaselineState.TriggerEntry(sourceId, perSource[sourceId]));
            }

            return entries;
        }

        private static IReadOnlyList<RecBaselineState.AnalogEntry> BuildBaselineAnalogEntries(
            RecIdTable idTable,
            List<(ushort sourceIndex, float[] axes)> baselineAnalogRecords)
        {
            var orderedSources = new List<string>();
            var latestAxes = new Dictionary<string, float[]>(StringComparer.Ordinal);

            for (int i = 0; i < baselineAnalogRecords.Count; i++)
            {
                if (!idTable.TryGetSourceId(baselineAnalogRecords[i].sourceIndex, out string sourceId))
                {
                    throw new InvalidOperationException($"Baseline analog referenced undefined source index {baselineAnalogRecords[i].sourceIndex}.");
                }

                if (!latestAxes.ContainsKey(sourceId))
                {
                    orderedSources.Add(sourceId);
                }

                latestAxes[sourceId] = baselineAnalogRecords[i].axes;
            }

            var entries = new List<RecBaselineState.AnalogEntry>(orderedSources.Count);
            for (int i = 0; i < orderedSources.Count; i++)
            {
                string sourceId = orderedSources[i];
                entries.Add(new RecBaselineState.AnalogEntry(sourceId, latestAxes[sourceId]));
            }

            return entries;
        }

        private static float[] CopyAxesToTemp(IReadOnlyList<float> axes)
        {
            if (axes == null || axes.Count == 0)
            {
                return Array.Empty<float>();
            }

            var copied = new float[axes.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = axes[i];
            }

            return copied;
        }
    }
}
