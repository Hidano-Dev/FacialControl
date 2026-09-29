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
        private readonly IAnalogInjectionPort _analogPort;
        private readonly RecPlaybackScheduler _scheduler = new RecPlaybackScheduler();

        private RecLoadResult _loadResult;
        private string[] _missingExpressionIds = Array.Empty<string>();

        public PlaybackUseCase(
            ITriggerInjectionPort triggerPort,
            IAnalogInjectionPort analogPort)
        {
            _triggerPort = triggerPort ?? throw new ArgumentNullException(nameof(triggerPort));
            _analogPort = analogPort ?? throw new ArgumentNullException(nameof(analogPort));
            State = RecPlaybackState.Idle;
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
            if (double.IsNaN(startOffsetSeconds) || double.IsInfinity(startOffsetSeconds) || startOffsetSeconds < 0d)
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
            _triggerPort.BeginInjection(baseline);
            _analogPort.BeginInjection(baseline);
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
            _analogPort.EndInjection();
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
    }
}
