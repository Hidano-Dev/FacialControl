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
        private const int DefaultByteCapacityPerSegment = 64;
        private const double ErrorLogThrottleSeconds = 5d;
        private const int ThreadJoinTimeoutMs = 2000;
        private const int OpenWaitTimeoutMs = 5000;
        private const int MaxOpenAttempts = 16;

        private readonly string _filePath;
        private readonly RecEventChunkQueue _queue;
        private readonly Func<string, Stream> _streamFactory;
        private readonly Action _postFinalizeAction;
        private readonly object _gate = new object();
        private readonly ManualResetEventSlim _openAttempted = new ManualResetEventSlim(false);

        private Thread _thread;
        private RecBaselineState _baseline = RecBaselineState.Empty;
        private long _startedAtUnixMilliseconds;
        private int _baselineRecordCount;
        private double _durationSeconds;
        private int _runtimeEventCount;
        private volatile bool _accepting;
        private volatile bool _stopRequested;
        private volatile string _outputFilePath;
        private volatile bool _outputFailed;
        private string _outputFailureMessage;
        private volatile bool _completeAbandoned;
        private int _failureReported;
        private bool _sessionOpen;
        private bool _disposed;

        public RecStreamWriter(
            string filePath,
            int segmentCapacity = DefaultSegmentCapacity,
            int initialSegments = DefaultInitialSegments,
            int axisFloatCapacityPerSegment = DefaultAxisFloatCapacityPerSegment,
            int byteCapacityPerSegment = DefaultByteCapacityPerSegment)
            : this(
                filePath,
                segmentCapacity,
                initialSegments,
                axisFloatCapacityPerSegment,
                byteCapacityPerSegment,
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
            : this(filePath, segmentCapacity, initialSegments, axisFloatCapacityPerSegment,
                DefaultByteCapacityPerSegment, streamFactory, postFinalizeAction)
        {
        }

        public RecStreamWriter(
            string filePath,
            int segmentCapacity,
            int initialSegments,
            int axisFloatCapacityPerSegment,
            int byteCapacityPerSegment,
            Func<string, Stream> streamFactory,
            Action postFinalizeAction)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A recording file path is required.", nameof(filePath));
            }

            _filePath = filePath;
            _queue = new RecEventChunkQueue(segmentCapacity, initialSegments, axisFloatCapacityPerSegment, byteCapacityPerSegment);
            _streamFactory = streamFactory ?? throw new ArgumentNullException(nameof(streamFactory));
            _postFinalizeAction = postFinalizeAction;
        }

        /// <summary>
        /// 直近のセッションで実際に開いた出力ファイルのパス（同名衝突時は連番付与後）。
        /// ライタースレッドがファイルを開くまでは null。
        /// </summary>
        public string OutputFilePath => _outputFilePath;

        /// <summary>
        /// 直近のセッションで出力ファイルを開けなかったか。ライタースレッドが判定するため
        /// <see cref="Open"/> 直後は false のことがある。true になったセッションではイベントを書き出さない。
        /// </summary>
        public bool HasOutputFailed => _outputFailed;

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
                _outputFilePath = null;
                _outputFailed = false;
                _outputFailureMessage = null;
                _completeAbandoned = false;
                _failureReported = 0;
                _openAttempted.Reset();
                _sessionOpen = true;
                _accepting = true;

                // 出力先の予約（連番付与）とファイルのオープンはライタースレッドで行い、呼び出し元
                // （通常は Unity メインスレッド）をストレージ I/O 待ちでブロックしない。
                // 開くまでに届いたイベントはキューに溜まり、オープン後にまとめて書き出される。
                _thread = new Thread(WriterLoop)
                {
                    IsBackground = true,
                    Name = "RecStreamWriter",
                };
                _thread.Start();
            }
        }

        public void AppendEvent(in RecEvent evt, ReadOnlySpan<float> payload, ReadOnlySpan<byte> maskBytes = default, string idValue = null)
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

            // payload（float）と mask（byte）はキューの別区画にコピーされ、ライタースレッドが RecBinaryFormat で書き出す。
            _queue.Enqueue(in evt, payload, maskBytes, idValue);
        }

        public void Complete(double durationSeconds, int eventCount)
        {
            Thread threadToJoin;
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
            }

            // オープンが遅いストレージでも結果（実パス / 失敗）を確定させてから終了処理に入るため、
            // 書き出しの Join とは別枠でオープン完了を待つ。
            if (threadToJoin != null
                && (!_openAttempted.Wait(OpenWaitTimeoutMs) || (threadToJoin.IsAlive && !threadToJoin.Join(ThreadJoinTimeoutMs))))
            {
                _completeAbandoned = true;
                Thread.MemoryBarrier();
                Debug.LogError($"REC writer timed out while finalizing '{_filePath}'.");
                ReportOutputFailureOnce();
                return;
            }

            if (_outputFailed)
            {
                ReportOutputFailureOnce();
                return;
            }

            Debug.Log($"REC writer finalized '{_outputFilePath}' with {_baselineRecordCount + eventCount} records. QueueGrowthCount={_queue.GrowthCount}.");
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
                stream = TryCreateStream(out string outputFilePath);
                if (stream == null)
                {
                    _accepting = false;
                    _outputFailed = true;
                }
                else
                {
                    _outputFilePath = outputFilePath;
                }

                _openAttempted.Set();

                // Complete が待ちきれずに戻った後で失敗が判明した場合は、ここでログを出す
                // （Complete 側と二重にならないよう ReportOutputFailureOnce で 1 回に絞る）。
                Thread.MemoryBarrier();
                if (stream == null && _completeAbandoned)
                {
                    ReportOutputFailureOnce();
                }

                if (stream != null)
                {
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
                }

                while (!_stopRequested || !_queue.IsEmpty)
                {
                    if (!_queue.TryDequeue(out RecEvent evt, out ReadOnlySpan<float> axes,
                        out ReadOnlySpan<byte> maskBytes, out string idValue))
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
                        WriteRecord(stream, ref buffer, in evt, axes, maskBytes, idValue);
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
            // RecordingUseCase と同じシードで IdDefine を書く（系1の予約 ID を含む）。食い違うと記録側の index が
            // ずれて読み戻しが失敗する。
            RecIdTable idTable = RecIdTable.CreateSeeded(_baseline);

            for (int i = 0; i < idTable.SourceIds.Count; i++)
            {
                WriteRecord(
                    stream,
                    ref buffer,
                    RecEvent.CreateIdDefine((ushort)i, RecEvent.IdDefinitionKind.Source),
                    ReadOnlySpan<float>.Empty,
                    ReadOnlySpan<byte>.Empty,
                    idTable.SourceIds[i]);
            }

            for (int i = 0; i < idTable.ExpressionIds.Count; i++)
            {
                WriteRecord(
                    stream,
                    ref buffer,
                    RecEvent.CreateIdDefine((ushort)i, RecEvent.IdDefinitionKind.Expression),
                    ReadOnlySpan<float>.Empty,
                    ReadOnlySpan<byte>.Empty,
                    idTable.ExpressionIds[i]);
            }

            for (int i = 0; i < _baseline.TriggerEntries.Count; i++)
            {
                RecBaselineState.TriggerEntry entry = _baseline.TriggerEntries[i];
                ushort sourceIndex = idTable.GetOrAddSourceId(entry.SourceId);
                for (int j = 0; j < entry.ExpressionIds.Count; j++)
                {
                    ushort expressionIndex = idTable.GetOrAddExpressionId(entry.ExpressionIds[j]);
                    WriteRecord(stream, ref buffer, RecEvent.CreateBaselineTrigger(sourceIndex, expressionIndex),
                        ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty, null);
                }
            }

            for (int i = 0; i < _baseline.AnalogEntries.Count; i++)
            {
                RecBaselineState.AnalogEntry entry = _baseline.AnalogEntries[i];
                ushort sourceIndex = idTable.GetOrAddSourceId(entry.SourceId);
                float[] axes = CopyAxes(entry.Axes);
                WriteRecord(stream, ref buffer, RecEvent.CreateBaselineAnalog(sourceIndex, checked((byte)axes.Length)), axes,
                    ReadOnlySpan<byte>.Empty, null);
            }

            for (int i = 0; i < _baseline.ValueProviderEntries.Count; i++)
            {
                RecBaselineState.ValueProviderEntry entry = _baseline.ValueProviderEntries[i];
                ushort sourceIndex = idTable.GetOrAddSourceId(entry.SourceId);
                float[] values = CopyValues(entry.Values);
                byte[] maskBytes = CopyMaskBytes(entry.MaskBytes);
                RecEvent evt = RecEvent.CreateBaselineValueProvider(sourceIndex, entry.IsValid,
                    checked((ushort)values.Length), checked((ushort)maskBytes.Length));
                WriteRecord(stream, ref buffer, in evt, values, maskBytes, null);
            }

            for (int i = 0; i < _baseline.ExpressionEntries.Count; i++)
            {
                ushort expressionIndex = idTable.GetOrAddExpressionId(_baseline.ExpressionEntries[i]);
                RecEvent evt = RecEvent.CreateBaselineExpression(0, expressionIndex);
                WriteRecord(stream, ref buffer, in evt, ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty, null);
            }
        }

        private static void WriteRecord(Stream stream, ref byte[] buffer, in RecEvent evt,
            ReadOnlySpan<float> values, ReadOnlySpan<byte> maskBytes, string idValue)
        {
            int requiredCapacity = evt.Kind == RecEventKind.IdDefine
                ? RecBinaryFormat.GetMaxRecordSize(GetUtf8ByteCount(idValue), 0, 0, 0)
                : RecBinaryFormat.GetMaxRecordSize(0,
                    evt.Kind == RecEventKind.AnalogSample || evt.Kind == RecEventKind.BaselineAnalog ? values.Length : 0,
                    values.Length, maskBytes.Length);
            EnsureBufferCapacity(ref buffer, requiredCapacity);
            int bytesWritten = RecBinaryFormat.WriteRecord(buffer, evt, values, maskBytes, idValue);
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
            RecIdTable idTable = RecIdTable.CreateSeeded(baseline);
            int count = idTable.SourceIds.Count + idTable.ExpressionIds.Count;

            for (int i = 0; i < baseline.TriggerEntries.Count; i++)
            {
                count += baseline.TriggerEntries[i].ExpressionIds.Count;
            }

            count += baseline.AnalogEntries.Count;
            count += baseline.ValueProviderEntries.Count;
            count += baseline.ExpressionEntries.Count;
            return count;
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

        private static float[] CopyValues(IReadOnlyList<float> values)
        {
            var copied = new float[values.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = values[i];
            }

            return copied;
        }

        private static byte[] CopyMaskBytes(IReadOnlyList<byte> maskBytes)
        {
            var copied = new byte[maskBytes.Count];
            for (int i = 0; i < copied.Length; i++)
            {
                copied[i] = maskBytes[i];
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

        private Stream TryCreateStream(out string outputFilePath)
        {
            outputFilePath = null;
            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // 同名テイクは上書きせず連番を付与して保存する（REC データは代替が効かない）。
                // 同じパスへ同時に録画を始めた別ライターに先を越された場合は、連番を取り直して再試行する。
                for (int attempt = 1; ; attempt++)
                {
                    string resolvedFilePath = RecSidecarPath.ResolveUniqueFilePath(_filePath);
                    try
                    {
                        Stream stream = _streamFactory(resolvedFilePath);
                        outputFilePath = resolvedFilePath;
                        return stream;
                    }
                    catch (IOException) when (attempt < MaxOpenAttempts && File.Exists(resolvedFilePath))
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                // ログは Complete で出す。_outputFailed（volatile）より先に書くので、失敗を観測した側から読める。
                _outputFailureMessage = ex.Message;
                return null;
            }
        }

        private void ReportOutputFailureOnce()
        {
            if (!_outputFailed || Interlocked.Exchange(ref _failureReported, 1) != 0)
            {
                return;
            }

            Debug.LogError($"REC writer could not open '{_filePath}': {_outputFailureMessage}");
        }

        private static Stream CreateFileStream(string filePath)
        {
            // 既存の録画を絶対に上書きしない。TryCreateStream が RecSidecarPath.ResolveUniqueFilePath で
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
