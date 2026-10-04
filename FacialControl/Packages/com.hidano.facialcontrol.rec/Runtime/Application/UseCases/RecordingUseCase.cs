using System;
using System.Collections;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Application.UseCases
{
    /// <summary>
    /// Coordinates a single recording session against the per-controller input observation bus.
    /// </summary>
    public sealed class RecordingUseCase : IFacialInputObserver, IDisposable
    {
        private readonly IFacialInputObservationBus _observationBus;
        private readonly IRecClock _clock;
        private readonly IRecEventSink _sink;
        private readonly double _startOffsetSeconds;

        private RecIdTable _idTable = new RecIdTable();
        private RecordingState _state;
        private int _eventCount;
        private double _lastClockSeconds;
        private bool _invalidClockWarned;
        private bool _disposed;
        private byte[] _maskScratch = Array.Empty<byte>();
        private float[] _valueScratch = Array.Empty<float>();

        /// <param name="startOffsetSeconds">
        /// 記録タイムスタンプと録画長に加算する開始オフセット（秒、有限かつ 0 以上）。
        /// クロックの値を検証してから加算するため、クロックの不正値がオフセットで隠れることはない。
        /// </param>
        public RecordingUseCase(
            IFacialInputObservationBus observationBus,
            IRecClock clock,
            IRecEventSink sink,
            double startOffsetSeconds = 0d)
        {
            _observationBus = observationBus ?? throw new ArgumentNullException(nameof(observationBus));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            if (!IsValidStartOffset(startOffsetSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(startOffsetSeconds), "Start offset must be a finite, non-negative number of seconds.");
            }

            _startOffsetSeconds = startOffsetSeconds;
            _state = RecordingState.Idle;
        }

        public double StartOffsetSeconds => _startOffsetSeconds;

        /// <summary>記録タイムスタンプは非負でなければならないため、有限かつ 0 以上だけを許す。</summary>
        public static bool IsValidStartOffset(double offsetSeconds)
        {
            return !double.IsNaN(offsetSeconds) && !double.IsInfinity(offsetSeconds) && offsetSeconds >= 0d;
        }

        public RecordingState State => _state;

        public bool IsRecording => _state == RecordingState.Recording;

        public double ElapsedSeconds => IsRecording ? SampleClock() : 0d;

        public void StartRecording(RecBaselineState baseline)
        {
            StartRecording(baseline, 0);
        }

        public void StartRecording(RecBaselineState baseline, int blendShapeCountHint)
        {
            ThrowIfDisposed();

            if (blendShapeCountHint < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(blendShapeCountHint));
            }

            if (IsRecording)
            {
                Debug.LogWarning("Recording is already active. StartRecording was ignored.");
                return;
            }

            baseline ??= RecBaselineState.Empty;
            _idTable = RecIdTable.CreateSeeded(baseline);
            EnsureScratchCapacity(blendShapeCountHint, (blendShapeCountHint + 7) / 8);
            _eventCount = 0;
            _clock.Reset();
            _lastClockSeconds = 0d;
            _invalidClockWarned = false;
            _sink.Open(baseline);
            _observationBus.Subscribe(this);
            _state = RecordingState.Recording;
        }

        public void StopRecording()
        {
            ThrowIfDisposed();

            if (!IsRecording)
            {
                return;
            }

            double durationSeconds = SampleClock();
            int eventCount = _eventCount;

            _state = RecordingState.Idle;
            _observationBus.Unsubscribe(this);
            _idTable = new RecIdTable();
            _eventCount = 0;

            _sink.Complete(durationSeconds, eventCount);
        }

        public void ToggleRecording(RecBaselineState baseline)
        {
            ThrowIfDisposed();

            if (IsRecording)
            {
                StopRecording();
                return;
            }

            StartRecording(baseline);
        }

        public void OnTriggerOn(string sourceId, string expressionId)
        {
            if (!IsRecording)
            {
                return;
            }

            if (!TryResolveTriggerIds(sourceId, expressionId, out ushort sourceIndex, out ushort expressionIndex))
            {
                return;
            }

            AppendEvent(RecEvent.CreateTriggerOn(SampleClock(), sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty);
        }

        public void OnTriggerOff(string sourceId, string expressionId)
        {
            if (!IsRecording)
            {
                return;
            }

            if (!TryResolveTriggerIds(sourceId, expressionId, out ushort sourceIndex, out ushort expressionIndex))
            {
                return;
            }

            AppendEvent(RecEvent.CreateTriggerOff(SampleClock(), sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty);
        }

        public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes)
        {
            if (!IsRecording)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                Debug.LogWarning("Recording ignored an analog sample because sourceId was null or empty.");
                return;
            }

            if (axes.Length <= 0 || axes.Length > byte.MaxValue)
            {
                Debug.LogWarning($"Recording ignored analog sample '{sourceId}' because axis count {axes.Length} was invalid.");
                return;
            }

            ushort sourceIndex = EnsureSourceIdDefined(sourceId);
            AppendEvent(RecEvent.CreateAnalogSample(SampleClock(), sourceIndex, checked((byte)axes.Length)), axes, ReadOnlySpan<byte>.Empty);
        }

        public void OnValueProviderSample(string sourceId, in ValueProviderSample sample)
        {
            if (!IsRecording || string.IsNullOrWhiteSpace(sourceId))
            {
                return;
            }

            RecValueProviderFlags flags = sample.IsValid ? RecValueProviderFlags.IsValid : RecValueProviderFlags.None;
            if (sample.MaskChanged)
            {
                flags |= RecValueProviderFlags.HasMask;
            }

            if (sample.ValuesChanged)
            {
                flags |= RecValueProviderFlags.HasValues;
            }

            // 差分形式: mask / values は変化したときだけ載せる。RecEvent は HasMask 無しの非ゼロ mask count を
            // 拒否するため、省略する成分の count は 0 にする。
            int fullMaskByteCount = (sample.ContributeMask.Length + 7) / 8;
            int maskByteCount = sample.MaskChanged ? fullMaskByteCount : 0;
            int valueCount = 0;
            if (sample.ValuesChanged)
            {
                for (int i = 0; i < sample.ContributeMask.Length; i++)
                {
                    if (sample.ContributeMask[i])
                    {
                        valueCount++;
                    }
                }
            }

            EnsureScratchCapacity(sample.ContributeMask.Length, fullMaskByteCount, valueCount);
            if ((flags & RecValueProviderFlags.HasMask) != 0)
            {
                Array.Clear(_maskScratch, 0, maskByteCount);
                for (int i = 0; i < sample.ContributeMask.Length; i++)
                {
                    if (sample.ContributeMask[i])
                    {
                        _maskScratch[i >> 3] |= (byte)(1 << (i & 7));
                    }
                }
            }

            if ((flags & RecValueProviderFlags.HasValues) != 0)
            {
                int packedIndex = 0;
                for (int i = 0; i < sample.ContributeMask.Length; i++)
                {
                    if (sample.ContributeMask[i])
                    {
                        _valueScratch[packedIndex++] = sample.Values[i];
                    }
                }
            }

            ushort sourceIndex = EnsureSourceIdDefined(sourceId);
            RecEvent evt = RecEvent.CreateValueProviderSample(
                SampleClock(), sourceIndex, flags, checked((ushort)valueCount), checked((ushort)maskByteCount));
            AppendEvent(evt,
                (flags & RecValueProviderFlags.HasValues) != 0 ? new ReadOnlySpan<float>(_valueScratch, 0, valueCount) : ReadOnlySpan<float>.Empty,
                (flags & RecValueProviderFlags.HasMask) != 0 ? new ReadOnlySpan<byte>(_maskScratch, 0, maskByteCount) : ReadOnlySpan<byte>.Empty);
        }

        public void OnExpressionActivated(string sourceId, string expressionId)
        {
            if (!IsRecording || !TryResolveTriggerIds(sourceId, expressionId, out ushort sourceIndex, out ushort expressionIndex))
            {
                return;
            }

            AppendEvent(RecEvent.CreateExpressionActivate(SampleClock(), sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty);
        }

        public void OnExpressionDeactivated(string sourceId, string expressionId)
        {
            if (!IsRecording || !TryResolveTriggerIds(sourceId, expressionId, out ushort sourceIndex, out ushort expressionIndex))
            {
                return;
            }

            AppendEvent(RecEvent.CreateExpressionDeactivate(SampleClock(), sourceIndex, expressionIndex), ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty);
        }

        public void OnLayerWeightSample(string layerName, float weight) { }

        public void OnInputSourceWeightSample(string layerName, string slotId, float weight) { }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (IsRecording)
            {
                _state = RecordingState.Idle;
                _observationBus.Unsubscribe(this);
                _idTable = new RecIdTable();
                _eventCount = 0;
            }

            _disposed = true;
        }

        /// <summary>
        /// クロックの現在値に開始オフセットを加えて記録用タイムスタンプとして読む。クロックは差し替え可能なので、
        /// オフセット加算前の値を検証し、例外・非有限・負の値は直前の値で置き換えて警告を 1 セッション 1 回だけ出し、
        /// 逆行はクランプする（逆行や負値のタイムスタンプを含むテイクは読み込めず、例外は入力の発行元まで伝播して
        /// しまうため）。置き換え時もオフセットは維持される。
        /// </summary>
        private double SampleClock()
        {
            double value;
            try
            {
                value = _clock.ElapsedSeconds;
            }
            catch (Exception ex)
            {
                WarnInvalidClockOnce($"threw {ex.GetType().Name}: {ex.Message}");
                return _lastClockSeconds + _startOffsetSeconds;
            }

            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0d)
            {
                WarnInvalidClockOnce($"returned {value}");
                return _lastClockSeconds + _startOffsetSeconds;
            }

            if (value > _lastClockSeconds)
            {
                _lastClockSeconds = value;
            }

            return _lastClockSeconds + _startOffsetSeconds;
        }

        private void WarnInvalidClockOnce(string detail)
        {
            if (_invalidClockWarned)
            {
                return;
            }

            _invalidClockWarned = true;
            Debug.LogWarning($"Recording clock {detail}. The previous timestamp {_lastClockSeconds + _startOffsetSeconds} was used instead.");
        }

        private bool TryResolveTriggerIds(string sourceId, string expressionId, out ushort sourceIndex, out ushort expressionIndex)
        {
            sourceIndex = 0;
            expressionIndex = 0;

            if (string.IsNullOrWhiteSpace(sourceId))
            {
                Debug.LogWarning("Recording ignored a trigger event because sourceId was null or empty.");
                return false;
            }

            if (string.IsNullOrWhiteSpace(expressionId))
            {
                Debug.LogWarning("Recording ignored a trigger event because expressionId was null or empty.");
                return false;
            }

            sourceIndex = EnsureSourceIdDefined(sourceId);
            expressionIndex = EnsureExpressionIdDefined(expressionId);
            return true;
        }

        private ushort EnsureSourceIdDefined(string sourceId)
        {
            bool existed = _idTable.TryGetSourceIndex(sourceId, out ushort index);
            index = _idTable.GetOrAddSourceId(sourceId);
            if (!existed)
            {
                AppendEvent(RecEvent.CreateIdDefine(index, RecEvent.IdDefinitionKind.Source), ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty, sourceId);
            }

            return index;
        }

        private ushort EnsureExpressionIdDefined(string expressionId)
        {
            bool existed = _idTable.TryGetExpressionIndex(expressionId, out ushort index);
            index = _idTable.GetOrAddExpressionId(expressionId);
            if (!existed)
            {
                AppendEvent(RecEvent.CreateIdDefine(index, RecEvent.IdDefinitionKind.Expression), ReadOnlySpan<float>.Empty, ReadOnlySpan<byte>.Empty, expressionId);
            }

            return index;
        }

        private void AppendEvent(in RecEvent evt, ReadOnlySpan<float> payload, ReadOnlySpan<byte> maskBytes, string idValue = null)
        {
            _sink.AppendEvent(evt, payload, maskBytes, idValue);
            _eventCount++;
        }

        private void EnsureScratchCapacity(int blendShapeCount, int maskByteCount, int valueCount = 0)
        {
            if (_valueScratch.Length < blendShapeCount || _valueScratch.Length < valueCount)
            {
                Array.Resize(ref _valueScratch, Math.Max(blendShapeCount, valueCount));
            }

            if (_maskScratch.Length < maskByteCount)
            {
                Array.Resize(ref _maskScratch, maskByteCount);
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RecordingUseCase));
            }
        }
    }
}
