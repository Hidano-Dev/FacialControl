using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Application.UseCases
{
    /// <summary>
    /// Coordinates timeline validation, baseline establishment, timed playback, and completion signaling.
    /// </summary>
    public sealed class PlaybackUseCase : IRecEventVisitor
    {
        private readonly ITriggerInjectionPort _triggerPort;
        private readonly IExpressionInjectionPort _expressionPort;
        private readonly IAnalogInjectionPort _analogPort;
        private readonly IValueProviderInjectionPort _valueProviderPort;
        private readonly RecPlaybackScheduler _scheduler = new RecPlaybackScheduler();

        private RecLoadResult _loadResult;
        private string[] _missingExpressionIds = Array.Empty<string>();

        public PlaybackUseCase(
            ITriggerInjectionPort triggerPort,
            IExpressionInjectionPort expressionPort,
            IAnalogInjectionPort analogPort,
            IValueProviderInjectionPort valueProviderPort)
        {
            _triggerPort = triggerPort ?? throw new ArgumentNullException(nameof(triggerPort));
            _expressionPort = expressionPort ?? throw new ArgumentNullException(nameof(expressionPort));
            _analogPort = analogPort ?? throw new ArgumentNullException(nameof(analogPort));
            _valueProviderPort = valueProviderPort ?? throw new ArgumentNullException(nameof(valueProviderPort));
            State = RecPlaybackState.Idle;
        }

        // Kept for source compatibility with the pre-four-port API. The production
        // binding uses the four-port constructor below.
        public PlaybackUseCase(ITriggerInjectionPort triggerPort, IAnalogInjectionPort analogPort)
            : this(triggerPort, NullExpressionInjectionPort.Instance, analogPort, NullValueProviderInjectionPort.Instance)
        {
        }

        public RecPlaybackState State { get; private set; }

        public double ElapsedSeconds => _scheduler.ElapsedSeconds;

        public event Action Completed;

        public RecLoadResult Load(RecTimeline timeline, FacialProfile profile)
        {
            if (timeline == null)
            {
                Debug.LogError("Playback load failed because timeline was null.");
                return null;
            }

            if (string.IsNullOrWhiteSpace(profile.SchemaVersion))
            {
                Debug.LogError("Playback load failed because profile was invalid.");
                return null;
            }

            StopPlayback();

            _loadResult = new RecLoadResult(timeline, RecValidation.FindMissingExpressionIds(timeline, profile));
            _missingExpressionIds = _loadResult.HasMissingExpressionIds
                ? CopyMissingExpressionIds(_loadResult.MissingExpressionIds)
                : Array.Empty<string>();
            _scheduler.Reset();
            State = RecPlaybackState.Idle;
            return _loadResult;
        }

        public bool StartPlayback()
        {
            return StartPlayback(0d);
        }

        /// <summary>
        /// Starts playback from <paramref name="startOffsetSeconds"/> seconds into the recording.
        /// Events before the offset are folded into the injected baseline (final trigger stacks and last analog values),
        /// so an expression that was mid-transition at the offset starts from its target state.
        /// </summary>
        public bool StartPlayback(double startOffsetSeconds)
        {
            if (!RecPlaybackScheduler.IsValidStartOffset(startOffsetSeconds))
            {
                Debug.LogWarning($"Playback start was ignored because startOffsetSeconds ({startOffsetSeconds}) must be a finite, non-negative number.");
                return false;
            }

            if (State == RecPlaybackState.Playing)
            {
                Debug.LogWarning("Playback is already active. StartPlayback was ignored.");
                return false;
            }

            if (_loadResult == null)
            {
                Debug.LogWarning("Playback start was ignored because no recording has been loaded.");
                return false;
            }

            LogMissingExpressionIdsOnce();

            RecTimeline timeline = _loadResult.Timeline;
            RecBaselineState baseline = CreateFilteredBaseline(RecTimelineSeek.BuildBaselineAt(timeline, startOffsetSeconds));

            IInjectionPort[] ports = { _triggerPort, _expressionPort, _analogPort, _valueProviderPort };
            string[] portNames = { "trigger", "expression", "analog", "valueProvider" };
            var failures = new List<string>(4);
            for (int i = 0; i < ports.Length; i++)
            {
                if (!ports[i].CanBeginInjection(out string reason))
                {
                    failures.Add($"{portNames[i]}: {reason}");
                }
            }

            if (failures.Count != 0)
            {
                Debug.LogError($"Playback start failed during injection preflight: {string.Join("; ", failures)}");
                return false;
            }

            if (State == RecPlaybackState.Completed)
            {
                _triggerPort.EndInjection();
                _expressionPort.EndInjection();
                _analogPort.EndInjection();
                _valueProviderPort.EndInjection();
                _scheduler.Reset();
                State = RecPlaybackState.Idle;
            }

            int begunCount = 0;
            for (int i = 0; i < ports.Length; i++)
            {
                if (ports[i].TryBeginInjection(baseline))
                {
                    begunCount++;
                    continue;
                }

                for (int rollback = begunCount - 1; rollback >= 0; rollback--)
                {
                    ports[rollback].EndInjection();
                }

                _scheduler.Reset();
                State = RecPlaybackState.Idle;
                Debug.LogError($"Playback start failed while establishing {portNames[i]} injection.");
                return false;
            }

            _scheduler.Load(timeline, startOffsetSeconds);

            if (_scheduler.IsCompleted)
            {
                State = RecPlaybackState.Completed;
                Completed?.Invoke();
                return true;
            }

            State = RecPlaybackState.Playing;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (State != RecPlaybackState.Playing)
            {
                return;
            }

            bool completed = _scheduler.Tick(deltaTime, this);
            if (!completed)
            {
                return;
            }

            State = RecPlaybackState.Completed;
            Completed?.Invoke();
        }

        public void StopPlayback()
        {
            if (State == RecPlaybackState.Idle)
            {
                return;
            }

            _triggerPort.EndInjection();
            _expressionPort.EndInjection();
            _analogPort.EndInjection();
            _valueProviderPort.EndInjection();
            _scheduler.Reset();
            State = RecPlaybackState.Idle;
        }

        public void VisitTriggerOn(string sourceId, string expressionId)
        {
            if (IsMissingExpressionId(expressionId))
            {
                return;
            }

            _triggerPort.InjectTriggerOn(sourceId, expressionId);
        }

        public void VisitTriggerOff(string sourceId, string expressionId)
        {
            if (IsMissingExpressionId(expressionId))
            {
                return;
            }

            _triggerPort.InjectTriggerOff(sourceId, expressionId);
        }

        public void VisitAnalogSample(string sourceId, ReadOnlySpan<float> axes)
        {
            _analogPort.InjectAnalogSample(sourceId, axes);
        }

        public void VisitValueProviderSample(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
        {
            _valueProviderPort.InjectValueProviderState(sourceId, isValid, maskBytes, values);
        }

        public void VisitExpressionActivate(string sourceId, string expressionId)
        {
            if (IsMissingExpressionId(expressionId))
            {
                return;
            }

            _expressionPort.InjectActivate(expressionId);
        }

        public void VisitExpressionDeactivate(string sourceId, string expressionId)
        {
            if (IsMissingExpressionId(expressionId))
            {
                return;
            }

            _expressionPort.InjectDeactivate(expressionId);
        }

        private void LogMissingExpressionIdsOnce()
        {
            for (int i = 0; i < _missingExpressionIds.Length; i++)
            {
                Debug.LogWarning($"Playback skipped missing expressionId '{_missingExpressionIds[i]}'.");
            }
        }

        private bool IsMissingExpressionId(string expressionId)
        {
            for (int i = 0; i < _missingExpressionIds.Length; i++)
            {
                if (string.Equals(_missingExpressionIds[i], expressionId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private RecBaselineState CreateFilteredBaseline(RecBaselineState baseline)
        {
            if (baseline == null || _missingExpressionIds.Length == 0)
            {
                return baseline ?? RecBaselineState.Empty;
            }

            IReadOnlyList<RecBaselineState.TriggerEntry> triggerEntries = baseline.TriggerEntries;
            IReadOnlyList<RecBaselineState.AnalogEntry> analogEntries = baseline.AnalogEntries;

            var filteredTriggers = new RecBaselineState.TriggerEntry[triggerEntries.Count];
            for (int i = 0; i < triggerEntries.Count; i++)
            {
                IReadOnlyList<string> originalIds = triggerEntries[i].ExpressionIds;
                var filteredIds = new List<string>(originalIds.Count);
                for (int j = 0; j < originalIds.Count; j++)
                {
                    string expressionId = originalIds[j];
                    if (!IsMissingExpressionId(expressionId))
                    {
                        filteredIds.Add(expressionId);
                    }
                }

                filteredTriggers[i] = new RecBaselineState.TriggerEntry(triggerEntries[i].SourceId, filteredIds);
            }

            var copiedAnalogs = new RecBaselineState.AnalogEntry[analogEntries.Count];
            for (int i = 0; i < analogEntries.Count; i++)
            {
                copiedAnalogs[i] = new RecBaselineState.AnalogEntry(analogEntries[i].SourceId, analogEntries[i].Axes);
            }

            return new RecBaselineState(filteredTriggers, copiedAnalogs);
        }

        private static string[] CopyMissingExpressionIds(IReadOnlyList<string> missingExpressionIds)
        {
            if (missingExpressionIds == null || missingExpressionIds.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copied = new string[missingExpressionIds.Count];
            for (int i = 0; i < missingExpressionIds.Count; i++)
            {
                copied[i] = missingExpressionIds[i];
            }

            return copied;
        }

        private sealed class NullExpressionInjectionPort : IExpressionInjectionPort
        {
            public static readonly NullExpressionInjectionPort Instance = new NullExpressionInjectionPort();
            public bool CanBeginInjection(out string reason) { reason = string.Empty; return true; }
            public bool TryBeginInjection(RecBaselineState baseline) { return true; }
            public void InjectActivate(string expressionId) { }
            public void InjectDeactivate(string expressionId) { }
            public void EndInjection() { }
        }

        private sealed class NullValueProviderInjectionPort : IValueProviderInjectionPort
        {
            public static readonly NullValueProviderInjectionPort Instance = new NullValueProviderInjectionPort();
            public bool CanBeginInjection(out string reason) { reason = string.Empty; return true; }
            public bool TryBeginInjection(RecBaselineState baseline) { return true; }
            public void InjectValueProviderState(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values) { }
            public void EndInjection() { }
        }
    }
}
