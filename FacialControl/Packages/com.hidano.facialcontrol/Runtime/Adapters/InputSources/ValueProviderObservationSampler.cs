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
    /// publish する source ID は Aggregator が渡す <paramref name="sourceId"/>（レイヤー内スロットの同定キー =
    /// レイヤー宣言の id = <c>InputSourceRegistry</c> の登録キー。<see cref="Services.LayerInputSourceRegistry"/> の slot id）を
    /// そのまま使う。同じインスタンスが複数の登録キーで宣言されていてもスロットごとに別 ID で記録され、
    /// REC の基準捕捉・注入（registry キーごとに 1 エントリ）と対応が取れる。
    /// </remarks>
    public sealed class ValueProviderObservationSampler : ILayerSourceValueObserver
    {
        private readonly IFacialInputObservationBus _bus;
        private readonly Dictionary<string, State> _states = new Dictionary<string, State>(StringComparer.Ordinal);

        public ValueProviderObservationSampler(IFacialInputObservationBus bus)
        {
            _bus = bus ?? throw new ArgumentNullException(nameof(bus));
        }

        /// <summary>前回の観測状態を破棄する。</summary>
        public void Reset()
        {
            _states.Clear();
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

            string id = sourceId.Value;
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
                // 無効 → 有効への復帰は mask と値を全量 publish する。無効中に録画を始めた基準は値を持たないため、
                // 復帰時に「有効性のみ」の差分しか残らないと、再生で有効化後もゼロのままになる。
                bool becameValid = !state.HasValidSample || !state.IsValid;
                maskChanged = becameValid || !MasksEqual(state.Mask, source.ContributeMask);
                valuesChanged = becameValid || !ValuesEqual(state.Values, preWeightValues);
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
