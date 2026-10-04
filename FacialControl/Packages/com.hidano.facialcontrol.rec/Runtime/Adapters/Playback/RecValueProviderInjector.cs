using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Playback
{
    /// <summary>値提供型入力源を REC 再生ソースへ置換する注入ポート。</summary>
    public sealed class RecValueProviderInjector : IValueProviderInjectionPort
    {
        private const string UnresolvedReason =
            "value-provider injection requires an initialised FacialController (BlendShapeCount is 0)";

        private readonly IInputSourceRegistry _registry;
        private readonly Func<int> _resolveBlendShapeCount;
        private readonly Dictionary<string, RecPlaybackValueProviderSource> _attachedSources =
            new Dictionary<string, RecPlaybackValueProviderSource>(StringComparer.Ordinal);
        private readonly HashSet<string> _warnedIds = new HashSet<string>(StringComparer.Ordinal);

        public RecValueProviderInjector(IInputSourceRegistry registry, Func<int> resolveBlendShapeCount)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _resolveBlendShapeCount = resolveBlendShapeCount ?? throw new ArgumentNullException(nameof(resolveBlendShapeCount));
        }

        public bool CanBeginInjection(out string reason)
        {
            if (_resolveBlendShapeCount() <= 0)
            {
                reason = UnresolvedReason;
                return false;
            }

            reason = string.Empty;
            return true;
        }

        public bool TryBeginInjection(RecBaselineState baseline)
        {
            EndInjection();
            _warnedIds.Clear();

            int blendShapeCount = _resolveBlendShapeCount();
            if (blendShapeCount <= 0)
            {
                return false;
            }

            IReadOnlyList<RecBaselineState.ValueProviderEntry> entries =
                (baseline ?? RecBaselineState.Empty).ValueProviderEntries;
            for (int i = 0; i < entries.Count; i++)
            {
                RecBaselineState.ValueProviderEntry entry = entries[i];
                if (!TryAttach(entry.SourceId, blendShapeCount, entry.IsValid, entry.MaskBytes, entry.Values))
                {
                    WarnOnce(entry.SourceId, "baseline state was invalid or the source id was unavailable");
                }
            }

            IReadOnlyList<string> registeredIds = _registry.RegisteredIds ?? Array.Empty<string>();
            for (int i = 0; i < registeredIds.Count; i++)
            {
                string sourceId = registeredIds[i];
                if (_attachedSources.ContainsKey(sourceId)
                    || !_registry.TryResolve(sourceId, out IInputSource source)
                    || !(source is ValueProviderInputSourceBase))
                {
                    continue;
                }

                TryAttach(sourceId, blendShapeCount, false, Array.Empty<byte>(), Array.Empty<float>());
            }

            return true;
        }

        public void InjectValueProviderState(string sourceId, bool isValid, ReadOnlySpan<byte> maskBytes, ReadOnlySpan<float> values)
        {
            if (_attachedSources.TryGetValue(sourceId, out RecPlaybackValueProviderSource source)
                && !source.ApplyState(isValid, maskBytes, values))
            {
                WarnOnce(sourceId, "value-provider state did not match the injected source shape");
            }
        }

        public void EndInjection()
        {
            foreach (KeyValuePair<string, RecPlaybackValueProviderSource> pair in _attachedSources)
            {
                string sourceId = pair.Key;
                RecPlaybackValueProviderSource playbackSource = pair.Value;
                if (!_registry.TryResolve(sourceId, out IInputSource currentSource)
                    || !ReferenceEquals(currentSource, playbackSource))
                {
                    WarnOnce(sourceId, "restoring was skipped because the registry entry is no longer owned by this injector");
                    continue;
                }

                if (!AdapterSlug.TryParseComposite(sourceId, out AdapterSlug slug, out string sub))
                {
                    WarnOnce(sourceId, "restoring was skipped because the source id is invalid");
                    continue;
                }

                if (playbackSource.ReplacedSource != null)
                {
                    Replace(slug, sub, playbackSource.ReplacedSource);
                }
                else
                {
                    Unregister(slug, sub);
                }
            }

            _attachedSources.Clear();
        }

        private bool TryAttach(string sourceId, int blendShapeCount, bool isValid,
            IReadOnlyList<byte> maskBytes, IReadOnlyList<float> values)
        {
            if (!AdapterSlug.TryParseComposite(sourceId, out AdapterSlug slug, out string sub))
            {
                return false;
            }

            bool hasCurrent = _registry.TryResolve(sourceId, out IInputSource currentSource);
            if (hasCurrent && currentSource is IInjectedInputSource)
            {
                WarnOnce(sourceId, "injection was skipped because another injected source occupies the id");
                return false;
            }

            var playbackSource = new RecPlaybackValueProviderSource(sourceId, blendShapeCount, hasCurrent ? currentSource : null);
            if (!playbackSource.ApplyState(isValid, ToSpan(maskBytes), ToSpan(values)))
            {
                playbackSource.ApplyState(false, ReadOnlySpan<byte>.Empty, ReadOnlySpan<float>.Empty);
                WarnOnce(sourceId, "baseline state did not match the injected source shape");
            }

            if (hasCurrent) Replace(slug, sub, playbackSource);
            else Register(slug, sub, playbackSource);
            _attachedSources[sourceId] = playbackSource;
            return true;
        }

        private static ReadOnlySpan<byte> ToSpan(IReadOnlyList<byte> values)
        {
            if (values == null || values.Count == 0) return ReadOnlySpan<byte>.Empty;
            var buffer = new byte[values.Count];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = values[i];
            return buffer;
        }

        private static ReadOnlySpan<float> ToSpan(IReadOnlyList<float> values)
        {
            if (values == null || values.Count == 0) return ReadOnlySpan<float>.Empty;
            var buffer = new float[values.Count];
            for (int i = 0; i < buffer.Length; i++) buffer[i] = values[i];
            return buffer;
        }

        private void Replace(AdapterSlug slug, string sub, IInputSource source)
        {
            if (string.IsNullOrEmpty(sub)) _registry.Replace(slug, source);
            else _registry.Replace(slug, sub, source);
        }

        private void Register(AdapterSlug slug, string sub, IInputSource source)
        {
            if (string.IsNullOrEmpty(sub)) _registry.Register(slug, source);
            else _registry.Register(slug, sub, source);
        }

        private void Unregister(AdapterSlug slug, string sub)
        {
            if (string.IsNullOrEmpty(sub)) _registry.Unregister(slug);
            else _registry.Unregister(slug, sub);
        }

        private void WarnOnce(string sourceId, string reason)
        {
            if (_warnedIds.Add(sourceId ?? "<null>"))
            {
                Debug.LogWarning($"Playback skipped value-provider injection for sourceId '{sourceId}': {reason}.");
            }
        }
    }
}
