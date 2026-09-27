using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.LipSync.Adapters
{
    public sealed class LipSyncPhonemeOverlayInputSource : ValueProviderInputSourceBase
    {
        public const string SlugPrefix = "lipsync-overlay";

        // provider が出す合成値（uLipSync 委譲後の volume × phoneme weight × snapshot）の総和が
        // この閾値を下回るフレームはレイヤー合成に乗せない。無音時に下位レイヤーを上書きしない契約。
        public const float SilenceThreshold = 1e-4f;

        // ContributeMask に取り込む閾値（ULipSyncProvider.ContributeThreshold と同値で整合）。
        private const float MaskThreshold = 1e-4f;

        private readonly string _phonemeId;
        private readonly ULipSyncProvider _provider;
        private readonly BitArray _defaultContributeMask;
        private readonly float[] _scratch;
        private readonly bool _phonemeRegistered;

        // ---- Override / Suppress 解決コンテキスト ----
        // Override は「音素の口形状 snapshot の差し替え」であり、駆動 weight
        // （音素 weight × 音量）は既定 snapshot と同じ値を使い回す（無音なら出力なし）。
        // Suppress は当該 slot の出力を止める。
        // precedence: Expression Override → Suppress → DefaultOverlays → LipSync default
        // （Documentation~/usage.md「表情側の Override / Suppress」）。
        // 解決コンテキスト未指定（旧 ctor）の場合は既定出力のみの従来動作。
        private readonly string _slot;
        private readonly IActiveExpressionProvider _activeProvider;
        private readonly string _emotionLayerName;
        private readonly Dictionary<string, ResolvedOverride> _overridesByExpressionId;
        private readonly bool _hasDefaultBinding;
        private readonly bool _defaultBindingSuppress;
        private readonly ResolvedOverride _defaultOverride;
        private BitArray _currentContributeMask;

        public LipSyncPhonemeOverlayInputSource(
            InputSourceId id,
            string phonemeId,
            ULipSyncProvider provider,
            int blendShapeCount)
            : this(id, phonemeId, provider, blendShapeCount, null, null, null, null, null)
        {
        }

        public LipSyncPhonemeOverlayInputSource(
            InputSourceId id,
            string phonemeId,
            ULipSyncProvider provider,
            int blendShapeCount,
            string slot,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile? resolutionProfile,
            IActiveExpressionProvider activeProvider,
            string emotionLayerName)
            : base(id, blendShapeCount)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            _phonemeId = phonemeId;
            _provider = provider;
            _scratch = new float[blendShapeCount];
            _phonemeRegistered = provider.TryGetPhonemeIndex(phonemeId, out _);
            _defaultContributeMask = _phonemeRegistered
                ? ResolveContributeMask(provider, phonemeId, blendShapeCount)
                : new BitArray(blendShapeCount, false);
            _currentContributeMask = _defaultContributeMask;
            _slot = slot;
            _activeProvider = activeProvider;
            _emotionLayerName = string.IsNullOrEmpty(emotionLayerName) ? "emotion" : emotionLayerName;

            if (!resolutionProfile.HasValue || string.IsNullOrEmpty(slot) || blendShapeNames == null)
            {
                _overridesByExpressionId = null;
                return;
            }

            FacialProfile profile = resolutionProfile.Value;
            Dictionary<string, int> nameToIndex = BuildNameToIndex(blendShapeNames);

            _overridesByExpressionId = new Dictionary<string, ResolvedOverride>(StringComparer.Ordinal);
            var exprSpan = profile.Expressions.Span;
            for (int i = 0; i < exprSpan.Length; i++)
            {
                var expression = exprSpan[i];
                if (string.IsNullOrEmpty(expression.Id)
                    || !expression.TryGetOverlay(slot, out OverlaySlotBinding binding)
                    || binding.Suppress
                    || !HasEffectiveSnapshot(binding))
                {
                    continue;
                }

                if (!_overridesByExpressionId.ContainsKey(expression.Id))
                {
                    _overridesByExpressionId.Add(
                        expression.Id,
                        ResolvedOverride.Build(binding.Snapshot.Value, nameToIndex, blendShapeCount));
                }
            }

            if (profile.TryGetDefaultOverlay(slot, out OverlaySlotBinding defaultBinding))
            {
                _defaultBindingSuppress = defaultBinding.Suppress;
                if (!defaultBinding.Suppress && HasEffectiveSnapshot(defaultBinding))
                {
                    _hasDefaultBinding = true;
                    _defaultOverride = ResolvedOverride.Build(
                        defaultBinding.Snapshot.Value, nameToIndex, blendShapeCount);
                }
                else if (defaultBinding.Suppress)
                {
                    _hasDefaultBinding = true;
                }
            }
        }

        public override BitArray ContributeMask => _currentContributeMask;

        public override bool TryWriteValues(Span<float> output)
        {
            if (!_phonemeRegistered)
            {
                return false;
            }

            OverlayResolution resolution = ResolveOverlayState(out ResolvedOverride overrideEntry);
            if (resolution == OverlayResolution.Suppress)
            {
                return false;
            }

            Span<float> scratch = _scratch;
            scratch.Clear();
            bool composed = resolution == OverlayResolution.Override
                ? _provider.TryComposePhonemeWeights(_phonemeId, overrideEntry.Weights, scratch)
                : _provider.TryComposePhonemeWeights(_phonemeId, scratch);
            if (!composed)
            {
                return false;
            }

            float sum = 0f;
            for (int i = 0; i < scratch.Length; i++)
            {
                sum += scratch[i];
            }

            if (sum < SilenceThreshold)
            {
                return false;
            }

            int copyLength = output.Length < scratch.Length ? output.Length : scratch.Length;
            for (int i = 0; i < copyLength; i++)
            {
                output[i] = scratch[i];
            }

            _currentContributeMask = resolution == OverlayResolution.Override
                ? overrideEntry.Mask
                : _defaultContributeMask;
            return true;
        }

        /// <summary>
        /// active 表情の phoneme binding → DefaultOverlays の順で自 slot の解決状態を求める。
        /// 毎フレーム呼ばれるが struct 取得と Dictionary lookup のみで GC アロケーションは発生しない。
        /// suppress=false かつ BlendShape を 1 つも持たない空 snapshot（Inspector 未設定のまま
        /// 出力された binding）は default fallback として扱う。
        /// </summary>
        private OverlayResolution ResolveOverlayState(out ResolvedOverride overrideEntry)
        {
            overrideEntry = default;
            if (_overridesByExpressionId == null)
            {
                return OverlayResolution.Default;
            }

            if (_activeProvider != null)
            {
                Expression? active = _activeProvider.TryGetTopActiveExpression(_emotionLayerName);
                if (active.HasValue && active.Value.TryGetOverlay(_slot, out OverlaySlotBinding activeBinding))
                {
                    if (activeBinding.Suppress)
                    {
                        return OverlayResolution.Suppress;
                    }

                    if (HasEffectiveSnapshot(activeBinding)
                        && !string.IsNullOrEmpty(active.Value.Id)
                        && _overridesByExpressionId.TryGetValue(active.Value.Id, out overrideEntry))
                    {
                        return OverlayResolution.Override;
                    }

                    // default fallback（snapshot 無し / 空 snapshot）は DefaultOverlays の判定へ続く。
                }
            }

            if (_hasDefaultBinding)
            {
                if (_defaultBindingSuppress)
                {
                    return OverlayResolution.Suppress;
                }

                overrideEntry = _defaultOverride;
                return OverlayResolution.Override;
            }

            return OverlayResolution.Default;
        }

        private static bool HasEffectiveSnapshot(in OverlaySlotBinding binding)
        {
            return binding.Snapshot.HasValue
                && binding.Snapshot.Value.BlendShapes.Length > 0;
        }

        private static Dictionary<string, int> BuildNameToIndex(IReadOnlyList<string> blendShapeNames)
        {
            var nameToIndex = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                string name = blendShapeNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!nameToIndex.ContainsKey(name))
                {
                    nameToIndex.Add(name, i);
                }
            }

            return nameToIndex;
        }

        private static BitArray ResolveContributeMask(
            ULipSyncProvider provider,
            string phonemeId,
            int blendShapeCount)
        {
            BitArray mask = provider.GetPhonemeContributeMask(phonemeId);
            if (mask == null)
            {
                return new BitArray(blendShapeCount, false);
            }

            if (mask.Length != blendShapeCount)
            {
                throw new ArgumentException(
                    "provider phoneme ContributeMask.Length must match blendShapeCount.",
                    nameof(provider));
            }

            return mask;
        }

        private enum OverlayResolution
        {
            Default,
            Override,
            Suppress,
        }

        /// <summary>
        /// Expression / DefaultOverlays の override snapshot を BlendShape index 空間の
        /// dense 配列へ事前解決したエントリ（per-frame 0-alloc で参照する）。
        /// </summary>
        private readonly struct ResolvedOverride
        {
            public readonly float[] Weights;
            public readonly BitArray Mask;

            private ResolvedOverride(float[] weights, BitArray mask)
            {
                Weights = weights;
                Mask = mask;
            }

            public static ResolvedOverride Build(
                ExpressionSnapshot snapshot,
                IReadOnlyDictionary<string, int> nameToIndex,
                int blendShapeCount)
            {
                var weights = new float[blendShapeCount];
                var mask = new BitArray(blendShapeCount, false);
                var blendShapes = snapshot.BlendShapes.Span;
                for (int i = 0; i < blendShapes.Length; i++)
                {
                    string name = blendShapes[i].Name;
                    if (string.IsNullOrEmpty(name) || !nameToIndex.TryGetValue(name, out int index))
                    {
                        continue;
                    }

                    weights[index] = blendShapes[i].Value;
                    float w = weights[index];
                    if (w < 0f) w = -w;
                    if (w > MaskThreshold)
                    {
                        mask[index] = true;
                    }
                }

                return new ResolvedOverride(weights, mask);
            }
        }
    }
}
