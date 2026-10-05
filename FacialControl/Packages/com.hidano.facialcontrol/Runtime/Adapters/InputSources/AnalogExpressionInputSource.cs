using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Adapters.InputSources
{
    /// <summary>
    /// Analog 入力 (LT/RT 等の連続値) を Expression 単位の weight として、
    /// Expression に含まれる全 BlendShape を 0..1 の連続値で滑らかに駆動する入力源 (Req: アナログ表情精密操作)。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="AnalogBlendShapeInputSource"/> が「analog axis 1 軸 → BlendShape 1 個」の
    /// per-BlendShape 直接マッピングなのに対し、本クラスは「analog axis 1 軸 → Expression 1 個 (= 複数 BlendShape)」を
    /// 一括スケールするための上位レイヤである。LT 半押し→ smile 表情の各 BlendShape が一律 50% で出力される、
    /// といった用途に使う。
    /// </para>
    /// <para>
    /// 構築時に <paramref name="bindings"/> から sourceId → <see cref="IAnalogInputSource"/> 解決と
    /// expressionId → BlendShape 値配列 (mask + 値ペア) を事前計算する。runtime ループでは
    /// 走査と加算のみ行い GC アロケーションを発生させない。
    /// </para>
    /// <para>
    /// <see cref="ContributeMask"/> は全 binding 対象 Expression の BlendShape mask の和集合。
    /// <see cref="TryWriteValues"/> は scalar &gt; 0 の有効 binding が 1 つでもあれば true を返し、
    /// 全 binding が無効なら false (Aggregator 側で空寄与扱い)。
    /// </para>
    /// <para>
    /// <see cref="IRegistryAttachableAnalogConsumer"/> を実装し、<see cref="AttachRegistry"/> で渡された registry の
    /// <c>{slug}:{SourceId}</c> を購読して、Replace / Unregister 通知に追従して読む先の source を差し替える
    /// （Timeline / REC の乗っ取りが BlendShape 出力まで届くための経路）。Attach しない限り従来どおり構築時の source を読む。
    /// </para>
    /// </remarks>
    public sealed class AnalogExpressionInputSource : ValueProviderInputSourceBase, IRegistryAttachableAnalogConsumer
    {
        /// <summary>本入力源の予約識別子。</summary>
        public const string ReservedId = "analog-expression";

        private readonly ResolvedBinding[] _resolvedBindings;
        private readonly BitArray _contributeMask;
        private readonly float[] _outputCache;

        private IInputSourceRegistry _attachedRegistry;
        private int _attachGeneration;

        /// <summary>
        /// <see cref="AnalogExpressionInputSource"/> を構築する。
        /// </summary>
        /// <param name="id">入力源識別子。</param>
        /// <param name="blendShapeCount">書込む BlendShape 個数 (&gt;= 0)。</param>
        /// <param name="blendShapeNames">BlendShape 名配列。expressionId 解決時に使用。</param>
        /// <param name="profile">Expression 検索元 <see cref="FacialProfile"/>。</param>
        /// <param name="sources">sourceId → <see cref="IAnalogInputSource"/> の辞書。</param>
        /// <param name="bindings">analog expression バインディング集合。</param>
        public AnalogExpressionInputSource(
            InputSourceId id,
            int blendShapeCount,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile profile,
            IReadOnlyDictionary<string, IAnalogInputSource> sources,
            IReadOnlyList<AnalogExpressionBinding> bindings)
            : base(id, blendShapeCount)
        {
            if (blendShapeNames == null) throw new ArgumentNullException(nameof(blendShapeNames));
            if (sources == null) throw new ArgumentNullException(nameof(sources));
            if (bindings == null) throw new ArgumentNullException(nameof(bindings));

            _contributeMask = new BitArray(blendShapeCount);
            _outputCache = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];

            // 名前 → index の逆引きマップ。
            var nameToIndex = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                var name = blendShapeNames[i];
                if (string.IsNullOrEmpty(name)) continue;
                if (!nameToIndex.ContainsKey(name)) nameToIndex[name] = i;
            }

            var resolved = new List<ResolvedBinding>(bindings.Count);
            for (int i = 0; i < bindings.Count; i++)
            {
                var b = bindings[i];
                if (string.IsNullOrEmpty(b.SourceId) || string.IsNullOrEmpty(b.ExpressionId))
                {
                    continue;
                }

                if (!sources.TryGetValue(b.SourceId, out var source) || source == null)
                {
                    Debug.LogWarning(
                        $"[AnalogExpressionInputSource] source '{b.SourceId}' not registered " +
                        $"(expressionId='{b.ExpressionId}'). Binding skipped.");
                    continue;
                }

                Expression? expr = profile.FindExpressionById(b.ExpressionId);
                if (!expr.HasValue)
                {
                    Debug.LogWarning(
                        $"[AnalogExpressionInputSource] expression '{b.ExpressionId}' not found in profile " +
                        $"(sourceId='{b.SourceId}'). Binding skipped.");
                    continue;
                }

                var bsValues = expr.Value.BlendShapeValues.Span;
                var indexList = new List<int>(bsValues.Length);
                var valueList = new List<float>(bsValues.Length);
                for (int v = 0; v < bsValues.Length; v++)
                {
                    if (!nameToIndex.TryGetValue(bsValues[v].Name, out int idx))
                    {
                        continue;
                    }
                    indexList.Add(idx);
                    valueList.Add(bsValues[v].Value);
                    _contributeMask[idx] = true;
                }

                resolved.Add(new ResolvedBinding(
                    sourceId: b.SourceId,
                    source: source,
                    sourceAxis: Math.Max(0, b.SourceAxis),
                    scale: b.Scale,
                    blendShapeIndices: indexList.ToArray(),
                    blendShapeValues: valueList.ToArray()));
            }

            _resolvedBindings = resolved.Count == 0
                ? Array.Empty<ResolvedBinding>()
                : resolved.ToArray();
        }

        /// <inheritdoc />
        public override BitArray ContributeMask => _contributeMask;

        /// <inheritdoc />
        public bool IsRegistryAttached => _attachedRegistry != null;

        /// <inheritdoc />
        public void AttachRegistry(IInputSourceRegistry registry, AdapterSlug slug)
        {
            if (registry == null)
            {
                throw new ArgumentNullException(nameof(registry));
            }
            if (slug.Value == null)
            {
                throw new ArgumentException("slug must be initialized.", nameof(slug));
            }

            if (ReferenceEquals(_attachedRegistry, registry))
            {
                return;
            }

            if (_attachedRegistry != null)
            {
                DetachRegistry();
            }

            _attachedRegistry = registry;
            int generation = ++_attachGeneration;

            for (int i = 0; i < _resolvedBindings.Length; i++)
            {
                ResolvedBinding binding = _resolvedBindings[i];
                string key = slug.Value + ":" + binding.SourceId;
                registry.Subscribe(key, notified =>
                {
                    // Detach / 再 Attach 後に届く旧世代の通知は無視する（Unsubscribe が無いため世代番号で no-op 化）。
                    if (generation != _attachGeneration)
                    {
                        return;
                    }

                    if (notified == null)
                    {
                        binding.Source = binding.OriginalSource;
                        return;
                    }

                    if (notified is IAnalogInputSource analog)
                    {
                        binding.Source = analog;
                    }
                });
            }
        }

        /// <inheritdoc />
        public void DetachRegistry()
        {
            if (_attachedRegistry == null)
            {
                return;
            }

            _attachGeneration++;
            _attachedRegistry = null;

            for (int i = 0; i < _resolvedBindings.Length; i++)
            {
                ResolvedBinding binding = _resolvedBindings[i];
                binding.Source = binding.OriginalSource;
            }
        }

        /// <inheritdoc />
        public override bool TryWriteValues(Span<float> output)
        {
            int resolvedCount = _resolvedBindings.Length;
            if (resolvedCount == 0)
            {
                return false;
            }

            Span<float> cache = _outputCache;
            cache.Clear();

            bool anyContribution = false;
            for (int i = 0; i < resolvedCount; i++)
            {
                var rb = _resolvedBindings[i];
                var source = rb.Source;
                if (!source.IsValid) continue;
                if (rb.SourceAxis >= source.AxisCount) continue;
                if (!TryReadAxis(source, rb.SourceAxis, out float raw)) continue;

                // analog scalar の負方向は 0 にクランプ (LT/RT は 0..1 を想定)。
                float scalar = raw < 0f ? 0f : raw;
                scalar *= rb.Scale;
                if (scalar <= 0f) continue;

                anyContribution = true;
                var indices = rb.BlendShapeIndices;
                var values = rb.BlendShapeValues;
                for (int k = 0; k < indices.Length; k++)
                {
                    cache[indices[k]] += values[k] * scalar;
                }
            }

            if (!anyContribution)
            {
                return false;
            }

            int copyLen = output.Length < cache.Length ? output.Length : cache.Length;
            for (int i = 0; i < copyLen; i++)
            {
                output[i] = cache[i];
            }

            return true;
        }

        private static bool TryReadAxis(IAnalogInputSource source, int axis, out float value)
        {
            if (axis < 0 || axis >= source.AxisCount)
            {
                value = 0f;
                return false;
            }

            if (source.AxisCount == 1)
            {
                return source.TryReadScalar(out value);
            }

            if (source.AxisCount == 2)
            {
                if (source.TryReadVector2(out float x, out float y))
                {
                    value = axis == 0 ? x : y;
                    return true;
                }
                value = 0f;
                return false;
            }

            Span<float> buf = stackalloc float[source.AxisCount];
            if (source.TryReadAxes(buf))
            {
                value = buf[axis];
                return true;
            }

            value = 0f;
            return false;
        }

        /// <summary>
        /// 構築時に解決した binding。<see cref="Source"/> は registry 通知で差し替わる現在の読み先、
        /// <see cref="OriginalSource"/> は構築時に解決した source（Unregister / Detach で戻す先）。
        /// </summary>
        private sealed class ResolvedBinding
        {
            public readonly string SourceId;
            public readonly IAnalogInputSource OriginalSource;
            public readonly int SourceAxis;
            public readonly float Scale;
            public readonly int[] BlendShapeIndices;
            public readonly float[] BlendShapeValues;

            public IAnalogInputSource Source;

            public ResolvedBinding(
                string sourceId,
                IAnalogInputSource source,
                int sourceAxis,
                float scale,
                int[] blendShapeIndices,
                float[] blendShapeValues)
            {
                SourceId = sourceId;
                OriginalSource = source;
                Source = source;
                SourceAxis = sourceAxis;
                Scale = scale;
                BlendShapeIndices = blendShapeIndices;
                BlendShapeValues = blendShapeValues;
            }
        }
    }

    /// <summary>
    /// <see cref="AnalogExpressionInputSource"/> 構築時に渡す Domain レベルの宣言的バインディング。
    /// </summary>
    public readonly struct AnalogExpressionBinding
    {
        public string SourceId { get; }
        public int SourceAxis { get; }
        public string ExpressionId { get; }
        public float Scale { get; }

        public AnalogExpressionBinding(string sourceId, int sourceAxis, string expressionId, float scale)
        {
            SourceId = sourceId ?? string.Empty;
            SourceAxis = sourceAxis;
            ExpressionId = expressionId ?? string.Empty;
            Scale = scale;
        }
    }
}
