using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>
    /// Injects playback-owned analog and gaze sources into the core registry.
    /// </summary>
    public sealed class RecAnalogInjector : IAnalogInjectionPort
    {
        private readonly IInputSourceRegistry _registry;
        private readonly Dictionary<string, RecPlaybackAnalogSource> _attachedSources =
            new Dictionary<string, RecPlaybackAnalogSource>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedOccupiedSourceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedRestoreSourceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedInvalidSourceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedAxisMismatchSourceIds =
            new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _loggedRegisteredSourceIds =
            new HashSet<string>(StringComparer.Ordinal);

        public RecAnalogInjector(IInputSourceRegistry registry)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public bool CanBeginInjection(out string reason)
        {
            reason = string.Empty;
            return true;
        }

        public bool TryBeginInjection(RecBaselineState baseline)
        {
            EndInjection();

            _warnedOccupiedSourceIds.Clear();
            _warnedRestoreSourceIds.Clear();
            _warnedInvalidSourceIds.Clear();
            _warnedAxisMismatchSourceIds.Clear();
            _loggedRegisteredSourceIds.Clear();

            IReadOnlyList<RecBaselineState.AnalogEntry> analogEntries =
                (baseline ?? RecBaselineState.Empty).AnalogEntries;

            for (int i = 0; i < analogEntries.Count; i++)
            {
                AttachPlaybackSource(analogEntries[i].SourceId, analogEntries[i].AxesAsSpan());
            }

            IReadOnlyList<string> registeredIds = _registry.RegisteredIds ?? Array.Empty<string>();
            for (int i = 0; i < registeredIds.Count; i++)
            {
                string sourceId = registeredIds[i];
                if (_attachedSources.ContainsKey(sourceId)
                    || !_registry.TryResolve(sourceId, out IInputSource source)
                    || source is not IAnalogInputSource analogSource)
                {
                    continue;
                }

                int axisCount = analogSource.AxisCount;
                if (axisCount <= 0)
                {
                    WarnAxisMismatchOnce(sourceId);
                    continue;
                }

                AttachPlaybackSource(sourceId, new float[axisCount]);
            }

            return true;
        }

        public void InjectAnalogSample(string sourceId, ReadOnlySpan<float> axes)
        {
            if (!_attachedSources.TryGetValue(sourceId, out RecPlaybackAnalogSource playbackSource))
            {
                return;
            }

            if (!playbackSource.SetAxes(axes))
            {
                WarnAxisMismatchOnce(sourceId);
            }
        }

        public void EndInjection()
        {
            if (_attachedSources.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, RecPlaybackAnalogSource> pair in _attachedSources)
            {
                string sourceId = pair.Key;
                RecPlaybackAnalogSource playbackSource = pair.Value;

                if (!TryParseRegistryKey(sourceId, out AdapterSlug slug, out string sub))
                {
                    WarnInvalidSourceIdOnce(sourceId);
                    continue;
                }

                if (!_registry.TryResolve(sourceId, out IInputSource currentSource)
                    || !ReferenceEquals(currentSource, playbackSource))
                {
                    WarnRestoreMismatchOnce(sourceId);
                    continue;
                }

                if (playbackSource.ReplacedSource != null)
                {
                    ReplaceSource(slug, sub, playbackSource.ReplacedSource);
                }
                else
                {
                    UnregisterSource(slug, sub);
                }
            }

            _attachedSources.Clear();
        }

        private static bool TryParseRegistryKey(string sourceId, out AdapterSlug slug, out string sub)
        {
            return AdapterSlug.TryParseComposite(sourceId, out slug, out sub);
        }

        private void AttachPlaybackSource(string sourceId, ReadOnlySpan<float> seedAxes)
        {
            if (!TryParseRegistryKey(sourceId, out AdapterSlug slug, out string sub))
            {
                WarnInvalidSourceIdOnce(sourceId);
                return;
            }

            int axisCount = seedAxes.Length;
            if (axisCount <= 0)
            {
                WarnAxisMismatchOnce(sourceId);
                return;
            }

            bool hasCurrent = _registry.TryResolve(sourceId, out IInputSource currentSource);
            if (currentSource is IInjectedInputSource)
            {
                WarnOccupiedOnce(sourceId);
                return;
            }

            var playbackSource = new RecPlaybackAnalogSource(sourceId, axisCount, hasCurrent ? currentSource : null);
            if (!playbackSource.SetAxes(seedAxes))
            {
                WarnAxisMismatchOnce(sourceId);
                return;
            }

            if (hasCurrent)
            {
                ReplaceSource(slug, sub, playbackSource);
            }
            else
            {
                RegisterSource(slug, sub, playbackSource);
                LogRegisteredWithoutOriginalOnce(sourceId);
            }

            _attachedSources[sourceId] = playbackSource;
        }

        private void RegisterSource(AdapterSlug slug, string sub, IInputSource source)
        {
            if (string.IsNullOrEmpty(sub))
            {
                _registry.Register(slug, source);
                return;
            }

            _registry.Register(slug, sub, source);
        }

        private void ReplaceSource(AdapterSlug slug, string sub, IInputSource source)
        {
            if (string.IsNullOrEmpty(sub))
            {
                _registry.Replace(slug, source);
                return;
            }

            _registry.Replace(slug, sub, source);
        }

        private void UnregisterSource(AdapterSlug slug, string sub)
        {
            if (string.IsNullOrEmpty(sub))
            {
                _registry.Unregister(slug);
                return;
            }

            _registry.Unregister(slug, sub);
        }

        private void LogRegisteredWithoutOriginalOnce(string sourceId)
        {
            if (_loggedRegisteredSourceIds.Add(sourceId))
            {
                Debug.Log($"Playback registered analog source '{sourceId}' because no live source was resolved.");
            }
        }

        private void WarnOccupiedOnce(string sourceId)
        {
            if (_warnedOccupiedSourceIds.Add(sourceId))
            {
                Debug.LogWarning(
                    $"Playback skipped analog injection for sourceId '{sourceId}' because another injected source already occupies it.");
            }
        }

        private void WarnRestoreMismatchOnce(string sourceId)
        {
            if (_warnedRestoreSourceIds.Add(sourceId))
            {
                Debug.LogWarning(
                    $"Playback skipped restoring analog source '{sourceId}' because the current registry entry is no longer owned by this playback injector.");
            }
        }

        private void WarnInvalidSourceIdOnce(string sourceId)
        {
            string normalizedSourceId = string.IsNullOrEmpty(sourceId) ? "<null-or-empty>" : sourceId;
            if (_warnedInvalidSourceIds.Add(normalizedSourceId))
            {
                Debug.LogWarning(
                    $"Playback skipped analog injection because sourceId '{normalizedSourceId}' is not a valid registry id.");
            }
        }

        private void WarnAxisMismatchOnce(string sourceId)
        {
            string normalizedSourceId = string.IsNullOrEmpty(sourceId) ? "<null-or-empty>" : sourceId;
            if (_warnedAxisMismatchSourceIds.Add(normalizedSourceId))
            {
                Debug.LogWarning(
                    $"Playback skipped analog sample for sourceId '{normalizedSourceId}' because axisCount did not match the injected playback source.");
            }
        }
    }

    internal static class RecBaselineStateAnalogEntryExtensions
    {
        public static ReadOnlySpan<float> AxesAsSpan(this RecBaselineState.AnalogEntry entry)
        {
            IReadOnlyList<float> axes = entry.Axes;
            var copied = new float[axes.Count];
            for (int i = 0; i < axes.Count; i++)
            {
                copied[i] = axes[i];
            }

            return copied;
        }
    }
}
