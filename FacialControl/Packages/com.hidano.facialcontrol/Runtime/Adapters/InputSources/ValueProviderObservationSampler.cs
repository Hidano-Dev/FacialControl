using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// Aggregator が消費した ValueProvider の値を、入力源ごとの最終 publish 状態と比較して通知する。
    /// </summary>
    /// <remarks>
    /// publish する source ID は <see cref="IInputSourceRegistry"/> の登録キー（レイヤー宣言の id。例: binding slug
    /// <c>ifm</c>）に解決する。Aggregator から渡される <paramref name="sourceId"/> は入力源自身の
    /// <see cref="IInputSource.Id"/>（<c>OscInputSource</c> は常に <c>osc</c>）で、REC の基準捕捉・注入が使う
    /// registry キーと一致しないことがあるため。registry に見つからない（レイヤーへ直接束ねられた）入力源は
    /// 従来どおり <paramref name="sourceId"/> を使う。
    /// </remarks>
    public sealed class ValueProviderObservationSampler : ILayerSourceValueObserver
    {
        private readonly IFacialInputObservationBus _bus;
        private readonly IInputSourceRegistry _registry;
        private readonly Dictionary<string, State> _states = new Dictionary<string, State>(StringComparer.Ordinal);
        private readonly Dictionary<IInputSource, string> _registryKeys =
            new Dictionary<IInputSource, string>(ReferenceComparer.Instance);

        public ValueProviderObservationSampler(IFacialInputObservationBus bus)
            : this(bus, null)
        {
        }

        /// <param name="registry">source ID の解決に使う per-FC registry。null なら Aggregator が渡した ID をそのまま使う。</param>
        public ValueProviderObservationSampler(IFacialInputObservationBus bus, IInputSourceRegistry registry)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
            _registry = registry;
        }

        /// <summary>前回の観測状態を破棄する。</summary>
        public void Reset()
        {
            _states.Clear();
            _registryKeys.Clear();
        }

        public void OnSourceValuesObserved(
            int layerIdx,
            int sourceIdx,
            IInputSource source,
            InputSourceId sourceId,
            bool isValid,
            ReadOnlySpan<float> preWeightValues)
        {
            if (!_bus.HasObservers || !(source is ValueProviderInputSourceBase))
            {
                return;
            }

            string id = ResolveObservedId(source, sourceId);
            if (string.IsNullOrEmpty(id))
            {
                return;
            }

            if (!_states.TryGetValue(id, out State state) || !ReferenceEquals(state.Source, source))
            {
                state = new State(source);
                _states[id] = state;
            }

            bool validityChanged = !state.HasSample || state.IsValid != isValid;
            bool valuesChanged = false;
            bool maskChanged = false;

            if (isValid)
            {
                maskChanged = !state.HasValidSample || !MasksEqual(state.Mask, source.ContributeMask);
                valuesChanged = !state.HasValidSample || !ValuesEqual(state.Values, preWeightValues);
                if (maskChanged)
                {
                    valuesChanged = true;
                }

                state.SetValidSample(preWeightValues, source.ContributeMask);
            }

            state.HasSample = true;
            state.IsValid = isValid;

            if (!validityChanged && !valuesChanged && !maskChanged)
            {
                return;
            }

            var sample = new ValueProviderSample(
                isValid,
                validityChanged,
                valuesChanged,
                maskChanged,
                preWeightValues,
                source.ContributeMask);
            _bus.PublishValueProviderSample(id, in sample);
        }

        /// <summary>
        /// 入力源インスタンスを registry の登録キーへ解決する。前回の解決結果は参照同一性で検証してから再利用し、
        /// 外れたとき（Replace / 後勝ち上書き後）だけ登録一覧を走査する。定常状態ではヒープ確保しない。
        /// </summary>
        private string ResolveObservedId(IInputSource source, InputSourceId sourceId)
        {
            if (_registry == null)
            {
                return sourceId.Value;
            }

            if (_registryKeys.TryGetValue(source, out string cachedKey)
                && _registry.TryResolve(cachedKey, out IInputSource cached)
                && ReferenceEquals(cached, source))
            {
                return cachedKey;
            }

            IReadOnlyList<string> registeredIds = _registry.RegisteredIds;
            if (registeredIds != null)
            {
                for (int i = 0; i < registeredIds.Count; i++)
                {
                    string candidate = registeredIds[i];
                    if (_registry.TryResolve(candidate, out IInputSource resolved) && ReferenceEquals(resolved, source))
                    {
                        _registryKeys[source] = candidate;
                        return candidate;
                    }
                }
            }

            _registryKeys.Remove(source);
            return sourceId.Value;
        }

        private static bool ValuesEqual(float[] previous, ReadOnlySpan<float> current)
        {
            if (previous == null || previous.Length != current.Length)
            {
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (BitConverter.SingleToInt32Bits(previous[i]) != BitConverter.SingleToInt32Bits(current[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool MasksEqual(BitArray previous, BitArray current)
        {
            if (previous == null || current == null || previous.Length != current.Length)
            {
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (previous[i] != current[i])
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class ReferenceComparer : IEqualityComparer<IInputSource>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();

            public bool Equals(IInputSource x, IInputSource y) => ReferenceEquals(x, y);

            public int GetHashCode(IInputSource obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private sealed class State
        {
            public State(IInputSource source)
            {
                Source = source;
            }

            public IInputSource Source { get; }
            public float[] Values { get; private set; }
            public BitArray Mask { get; private set; }
            public bool HasValidSample { get; private set; }
            public bool HasSample;
            public bool IsValid;

            public void SetValidSample(ReadOnlySpan<float> values, BitArray mask)
            {
                if (Values == null || Values.Length != values.Length)
                {
                    Values = new float[values.Length];
                }

                values.CopyTo(Values);

                if (Mask == null || Mask.Length != mask.Length)
                {
                    Mask = new BitArray(mask.Length);
                }

                for (int i = 0; i < mask.Length; i++)
                {
                    Mask[i] = mask[i];
                }

                HasValidSample = true;
            }
        }
    }
}
