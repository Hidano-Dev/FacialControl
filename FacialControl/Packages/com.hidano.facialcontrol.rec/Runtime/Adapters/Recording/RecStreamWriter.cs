using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Recording
{
    /// <summary>
    /// Streams REC records to disk on a dedicated writer thread.
    /// </summary>
    public sealed class RecStreamWriter : IRecEventSink, IDisposable
    {
        private const int DefaultSegmentCapacity = 64;
        private const int DefaultInitialSegments = 4;
        private const int DefaultAxisFloatCapacityPerSegment = 128;
        private const double ErrorLogThrottleSeconds = 5d;
        private const int ThreadJoinTimeoutMs = 2000;

        private readonly string _filePath;
        private readonly RecEventChunkQueue _queue;
        private readonly Func<string, Stream> _streamFactory;
        private readonly Action _postFinalizeAction;
        private readonly object _gate = new object();

        private Thread _thread;
        private Stream _pendingStream;
        private RecBaselineState _baseline = RecBaselineState.Empty;
        private long _startedAtUnixMilliseconds;
        private int _baselineRecordCount;
        private double _durationSeconds;
        private int _runtimeEventCount;
        private volatile bool _accepting;
        private volatile bool _stopRequested;
        private bool _sessionOpen;
        private bool _outputAvailable;
        private bool _disposed;

        public RecStreamWriter(
            string filePath,
            int segmentCapacity = DefaultSegmentCapacity,
            int initialSegments = DefaultInitialSegments,
            int axisFloatCapacityPerSegment = DefaultAxisFloatCapacityPerSegment)
            : this(
                filePath,
                segmentCapacity,
                initialSegments,
                axisFloatCapacityPerSegment,
                CreateFileStream,
                CreatePostFinalizeAction())
        {
        }

        public RecStreamWriter(
            string filePath,
            int segmentCapacity,
            int initialSegments,
            int axisFloatCapacityPerSegment,
            Func<string, Stream> streamFactory,
            Action postFinalizeAction)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A recording file path is required.", nameof(filePath));
            }

            _filePath = filePath;
            _queue = new RecEventChunkQueue(segmentCapacity, initialSegments, axisFloatCapacityPerSegment);
            _streamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
            _postFinalizeAction = postFinalizeAction;
        }

        /// <summary>
        /// 直近の <see cref="Open"/> で出力ファイルを開けたか。false のセッションではイベントを書き出さない。
        /// </summary>
        public bool IsOutputAvailable => _outputAvailable;

        public void Open(RecBaselineState baseline)
        {
            ThrowIfDisposed();

            lock (_gate)
            {
                if (_sessionOpen)
                {
                    Debug.LogWarning($"REC writer ignored Open because a session was already active for '{_filePath}'.");
                    return;
                }

                _baseline = baseline ?? RecBaselineState.Empty;
                _startedAtUnixMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _baselineRecordCount = CountBaselineRecords(_baseline);
                _durationSeconds = 0d;
                _runtimeEventCount = 0;
                _stopRequested = false;
                _sessionOpen = true;

                // 出力ファイルは呼び出し元スレッドで同期的に開く。失敗（既存ファイルとの衝突など）を
                // 呼び出し元が IsOutputAvailable で検知できるようにするため、ライタースレッドには委ねない。
                _pendingStream = TryCreateStream();
                _outputAvailable = _pendingStream != null;
                _accepting = _outputAvailable;
                if (!_outputAvailable)
                {
                    return;
                }

                _thread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "RecStreamWriter",
                };
                _thread.Start();
            }
        }

        public void AppendEvent(in RecEvent evt, ReadOnlySpan<float> axes, string idValue = null)
        {
            if (!_accepting)
            {
                return;
            }

            if (evt.Kind == RecEventKind.IdDefine && string.IsNullOrWhiteSpace(idValue))
            {
                Debug.LogError("REC writer ignored an IdDefine record because idValue was null or empty.");
                return;
            }

            _queue.Enqueue(in evt, axes, idValue);
        }

        public void Complete(double durationSeconds, int eventCount)
        {
            Thread threadToJoin;
            bool outputAvailable;
            lock (_gate)
            {
                if (!_sessionOpen)
                {
                    return;
                }

                _accepting = false;
                _stopRequested = true;
                _durationSeconds = durationSeconds;
                _runtimeEventCount = eventCount;
                threadToJoin = _thread;
                _thread = null;
                _sessionOpen = false;
                outputAvailable = _outputAvailable;
            }

            if (!outputAvailable)
            {
                return;
            }

            if (threadToJoin != null && threadToJoin.IsAlive && !threadToJoin.Join(ThreadJoinTimeoutMs))
            {
                Debug.LogError($"REC writer timed out while finalizing '{_filePath}'.");
                return;
            }

            Debug.Log($"REC writer finalized '{_filePath}' with {_baselineRecordCount + eventCount} records. QueueGrowthCount={_queue.GrowthCount}.");
            _postFinalizeAction?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            Complete(_durationSeconds, _runtimeEventCount);
            _disposed = true;
        }

        private void WriterLoop()
        {
            Stream stream = null;
            byte[] buffer = new byte[Math.Max(RecBinaryFormat.HeaderSize, RecBinaryFormat.FooterRecordSize)];
            DateTime startedAtUtc = DateTime.UtcNow;
            double nextErrorLogSeconds = 0d;

            try
            {
                stream = _pendingStream;
                _pendingStream = null;

                try
                {
                    WriteHeader(stream, ref buffer);
                    WriteBaseline(stream, ref buffer);
                }
                catch (Exception ex)
                {
                    LogThrottledError(ex, startedAtUtc, ref nextErrorLogSeconds);
                    stream.Dispose();
                    stream = null;
                }

                while (!_stopRequested || !_queue.IsEmpty)
                {
                    if (!_queue.TryDequeue(out RecEvent evt, out ReadOnlySpan<float> axes, out string idValue))
                    {
                        Thread.Yield();
                        continue;
                    }

                    if (stream == null)
                    {
                        continue;
                    }

                    try
                    {
                        WriteRecord(stream, ref buffer, in evt, axes, idValue);
                    }
                    catch (Exception ex)
                    {
                        LogThrottledError(ex, startedAtUtc, ref nextErrorLogSeconds);
                        stream.Dispose();
                        stream = null;
                    }
                }

                if (stream != null)
                {
                    try
                    {
                        WriteFooter(stream, ref buffer, _durationSeconds, checked((uint)(_baselineRecordCount + _runtimeEventCount)));
                    }
                    catch (Exception ex)
                    {
                        LogThrottledError(ex, startedAtUtc, ref nextErrorLogSeconds);
                    }
                }
            }
            finally
            {
                stream?.Dispose();
            }
        }

        private void WriteHeader(Stream stream, ref byte[] buffer)
        {
            EnsureBufferCapacity(ref buffer, RecBinaryFormat.HeaderSize);
            int bytesWritten = RecBinaryFormat.WriteHeader(buffer, _startedAtUnixMilliseconds);
            stream.Write(buffer, 0, bytesWritten);
        }

        private void WriteBaseline(Stream stream, ref byte[] buffer)
        {
            var idTable = CreateSeededIdTable(_baseline);

            for (int i = 0; i < idTable.SourceIds.Count; i++)
            {
                WriteRecord(
                    stream,
                    ref buffer,
                    RecEvent.CreateIdDefine((ushort)i, RecEvent.IdDefinitionKind.Source),
                    ReadOnlySpan<float>.Empty,
                    idTable.SourceIds[i]);
            }

            for (int i = 0; i < idTable.ExpressionIds.Count; i++)
            {
                WriteRecord(
                    stream,
                    ref buffer,
                    RecEvent.CreateIdDefine((ushort)i, RecEvent.IdDefinitionKind.Expression),
                    ReadOnlySpan<float>.Empty,
                    idTable.ExpressionIds[i]);
            }

            for (int i = 0; i < _baseline.TriggerEntries.Count; i++)
            {
                RecBaselineState.TriggerEntry entry = _baseline.TriggerEntries[i];
                ushort sourceIndex = idTable.GetOrAddSourceId(entry.SourceId);
                for (int j = 0; j < entry.ExpressionIds.Count; j++)
                {
                    ushort expressionIndex = idTable.GetOrAddExpressionId(entry.ExpressionIds[j]);
                    WriteRecord(stream, ref buffer, RecEvent.CreateBaselineTrigger(sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty, null);
                }
            }

            for (int i = 0; i < _baseline.AnalogEntries.Count; i++)
            {
                RecBaselineState.AnalogEntry entry = _baseline.AnalogEntries[i];
                ushort sourceIndex = idTable.GetOrAddSourceId(entry.SourceId);
                float[] axes = CopyAxes(entry.Axes);
                WriteRecord(stream, ref buffer, RecEvent.CreateBaselineAnalog(sourceIndex, checked((byte)axes.Length)), axes, null);
            }
        }

        private static void WriteRecord(Stream stream, ref byte[] buffer, in RecEvent evt, ReadOnlySpan<float> axes, string idValue)
        {
            int requiredCapacity = evt.Kind == RecEventKind.IdDefine
                ? RecBinaryFormat.GetMaxRecordSize(GetUtf8ByteCount(idValue), 0)
                : RecBinaryFormat.GetMaxRecordSize(0, axes.Length);
            EnsureBufferCapacity(ref buffer, requiredCapacity);
            int bytesWritten = RecBinaryFormat.WriteRecord(buffer, evt, axes, idValue);
            stream.Write(buffer, 0, bytesWritten);
        }

        private static void WriteFooter(Stream stream, ref byte[] buffer, double durationSeconds, uint recordCount)
        {
            EnsureBufferCapacity(ref buffer, RecBinaryFormat.FooterRecordSize);
            int bytesWritten = RecBinaryFormat.WriteFooter(buffer, durationSeconds, recordCount);
            stream.Write(buffer, 0, bytesWritten);
            stream.Flush();
        }

        private static int CountBaselineRecords(RecBaselineState baseline)
        {
            var idTable = CreateSeededIdTable(baseline);
            int count = idTable.SourceIds.Count + idTable.ExpressionIds.Count;

            for (int i = 0; i < baseline.TriggerEntries.Count; i++)
            {
                count += baseline.TriggerEntries[i].ExpressionIds.Count;
            }

            count += baseline.AnalogEntries.Count;
            return count;
        }

        private static RecIdTable CreateSeededIdTable(RecBaselineState baseline)
        {
            var idTable = new RecIdTable();
            if (baseline == null)
            {
                return idTable;
            }

            for (int i = 0; i < baseline.TriggerEntries.Count; i++)
            {
                RecBaselineState.TriggerEntry entry = baseline.TriggerEntries[i];
                idTable.GetOrAddSourceId(entry.SourceId);
                for (int j = 0; j < entry.ExpressionIds.Count; j++)
                {
                    idTable.GetOrAddExpressionId(entry.ExpressionIds[j]);
                }
            }

            for (int i = 0; i < baseline.AnalogEntries.Count; i++)
            {
                idTable.GetOrAddSourceId(baseline.AnalogEntries[i].SourceId);
            }

            return idTable;
        }

        private static float[] CopyAxes(IReadOnlyList<float> axes)
        {
            var copied = new float[axes.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = axes[i];
            }

            return copied;
        }

        private static int GetUtf8ByteCount(string value)
        {
            return string.IsNullOrEmpty(value) ? 0 : Encoding.UTF8.GetByteCount(value);
        }

        private static void EnsureBufferCapacity(ref byte[] buffer, int requiredCapacity)
        {
            if (buffer.Length >= requiredCapacity)
            {
                return;
            }

            Array.Resize(ref buffer, requiredCapacity);
        }

        private Stream TryCreateStream()
        {
            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                return _streamFactory(_filePath);
            }
            catch (Exception ex)
            {
                Debug.LogError($"REC writer could not open '{_filePath}': {ex.Message}");
                return null;
            }
        }

        private static Stream CreateFileStream(string filePath)
        {
            // 既存の録画を絶対に上書きしない。呼び出し側が RecSidecarPath.ResolveUniqueFilePath で
            // 衝突を避けている前提だが、競合した場合も CreateNew が失敗してファイルを守る。
            return new FileStream(filePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        }

        private static Action CreatePostFinalizeAction()
        {
            #if UNITY_EDITOR
            return RefreshAssetDatabase;
            #else
            return null;
            #endif
        }

        #if UNITY_EDITOR
        private static void RefreshAssetDatabase()
        {
            Type assetDatabaseType = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
            assetDatabaseType?.GetMethod("Refresh", Type.EmptyTypes)?.Invoke(null, null);
        }
        #endif

        private static void LogThrottledError(Exception exception, DateTime startedAtUtc, ref double nextErrorLogSeconds)
        {
            double elapsedSeconds = (DateTime.UtcNow - startedAtUtc).TotalSeconds;
            if (elapsedSeconds < nextErrorLogSeconds)
            {
                return;
            }

            Debug.LogError($"REC writer I/O failed: {exception.Message}");
            nextErrorLogSeconds = elapsedSeconds + ErrorLogThrottleSeconds;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RecStreamWriter));
            }
        }
    }
}
