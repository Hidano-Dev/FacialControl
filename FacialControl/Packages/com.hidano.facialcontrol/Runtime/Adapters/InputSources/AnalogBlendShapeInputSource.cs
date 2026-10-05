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
    /// 予約 id <c>analog-blendshape</c> を持つ BlendShape 値提供型アダプタ
    /// 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 1 つ以上の <see cref="IAnalogInputSource"/> から軸値を読出し、
    /// binding が指定する BlendShape index に**加算**する（二重 clamp なし）。
    /// dead-zone / scale / offset / curve / invert / clamp の値変換は Adapters 側 InputProcessor 経路で
    /// 上流処理されるため、本アダプタは生値をそのまま反映する。
    /// </para>
    /// <para>
    /// 構築時に bindings の <see cref="AnalogBindingEntry.TargetIdentifier"/> を BlendShape index に逆引きキャッシュする
    /// 。未存在 BS は <see cref="Debug.LogWarning(object)"/> + skip。
    /// 内部出力バッファ <c>_outputCache</c> を 1 度だけ確保し、毎フレーム再利用する。
    /// </para>
    /// <para>
    /// 全 binding が無効ソース (<see cref="IAnalogInputSource.IsValid"/>=false / 未登録 source) の場合、
    /// <see cref="TryWriteValues"/> は false を返し <c>output</c> を変更しない（IInputSource 契約）。
    /// 1 件でも書込が発生した場合は true を返す。
    /// </para>
    /// <para>
    /// <see cref="IRegistryAttachableAnalogConsumer"/> を実装し、<see cref="AttachRegistry"/> で渡された registry の
    /// <c>{slug}:{SourceId}</c> を購読して、Replace / Unregister 通知に追従して読む先の source を差し替える
    /// （<see cref="AnalogExpressionInputSource"/> と同じ規則）。Attach しない限り従来どおり構築時の source を読む。
    /// </para>
    /// </remarks>
    public sealed class AnalogBlendShapeInputSource : ValueProviderInputSourceBase, IRegistryAttachableAnalogConsumer
    {
        /// <summary>本アダプタの予約識別子。</summary>
        public const string ReservedId = "analog-blendshape";

        private readonly IReadOnlyDictionary<string, IAnalogInputSource> _sources;
        private readonly ResolvedBinding[] _resolvedBindings;
        private readonly BitArray _contributeMask;
        private readonly float[] _outputCache;

        private IInputSourceRegistry _attachedRegistry;
        private int _attachGeneration;

        /// <summary>
        /// <see cref="AnalogBlendShapeInputSource"/> を構築する。
        /// </summary>
        /// <param name="id">入力源識別子（典型的に <c>analog-blendshape</c>）。</param>
        /// <param name="blendShapeCount">書込む BlendShape 個数（&gt;= 0）。</param>
        /// <param name="blendShapeNames">BlendShape 名配列（index で配置）。</param>
        /// <param name="sources">sourceId → <see cref="IAnalogInputSource"/> の辞書。</param>
        /// <param name="bindings">バインディング集合。<see cref="AnalogBindingTargetKind.BlendShape"/> のみ採用。</param>
        public AnalogBlendShapeInputSource(
            InputSourceId id,
            int blendShapeCount,
            IReadOnlyList<string> blendShapeNames,
            IReadOnlyDictionary<string, IAnalogInputSource> sources,
            IReadOnlyList<AnalogBindingEntry> bindings)
            : base(id, blendShapeCount)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }
            if (sources == null)
            {
                throw new ArgumentNullException(nameof(sources));
            }
            if (bindings == null)
            {
                throw new ArgumentNullException(nameof(bindings));
            }

            _sources = sources;
            _contributeMask = new BitArray(blendShapeCount);
            _outputCache = blendShapeCount == 0 ? Array.Empty<float>() : new float[blendShapeCount];

            // BlendShape 名 → index の逆引きマップを 1 度だけ構築する。
            // 同名重複時は最初のヒットを優先（FacialController 側の慣習）。
            var nameToIndex = new Dictionary<string, int>(blendShapeNames.Count, StringComparer.Ordinal);
            for (int i = 0; i < blendShapeNames.Count; i++)
            {
                var name = blendShapeNames[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }
                if (!nameToIndex.ContainsKey(name))
                {
                    nameToIndex[name] = i;
                }
            }

            // BlendShape ターゲットの bindings のみ抽出し、(sourceId → source 解決 + index 解決) を init 時に確定する。
            var resolvedList = new List<ResolvedBinding>(bindings.Count);
            for (int i = 0; i < bindings.Count; i++)
            {
                var entry = bindings[i];
                if (entry.TargetKind != AnalogBindingTargetKind.BlendShape)
                {
                    continue;
                }

                if (!nameToIndex.TryGetValue(entry.TargetIdentifier, out int bsIndex))
                {
                    Debug.LogWarning(
                        $"[AnalogBlendShapeInputSource] BlendShape '{entry.TargetIdentifier}' not found " +
                        $"(sourceId={entry.SourceId}, sourceAxis={entry.SourceAxis}). Binding skipped .");
                    continue;
                }

                if (!_sources.TryGetValue(entry.SourceId, out var source) || source == null)
                {
                    Debug.LogWarning(
                        $"[AnalogBlendShapeInputSource] source '{entry.SourceId}' not registered " +
                        $"(target='{entry.TargetIdentifier}'). Binding skipped.");
                    continue;
                }

                resolvedList.Add(new ResolvedBinding(entry.SourceId, source, entry.SourceAxis, bsIndex, entry.Scale, entry.Direction));
                _contributeMask[bsIndex] = true;
            }

            _resolvedBindings = resolvedList.Count == 0
                ? Array.Empty<ResolvedBinding>()
                : resolvedList.ToArray();
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

            // 内部 cache を毎フレームクリアし、有効 binding ごとに sum で加算する。
            Span<float> cache = _outputCache;
            cache.Clear();

            bool anyValid = false;
            for (int i = 0; i < resolvedCount; i++)
            {
                var rb = _resolvedBindings[i];
                var source = rb.Source;
                if (!source.IsValid)
                {
                    continue;
                }

                if (rb.SourceAxis >= source.AxisCount)
                {
                    continue;
                }

                if (!TryReadAxis(source, rb.SourceAxis, out float raw))
                {
                    continue;
                }

                // 入力符号フィルタを適用してから scale 倍率で BS index に加算する（gaze 4 系統等の用途）。
                // Bipolar 既定では従来挙動と完全互換（raw * 1f）。
                float effective;
                switch (rb.Direction)
                {
                    case AnalogBindingDirection.Positive:
                        effective = raw > 0f ? raw : 0f;
                        break;
                    case AnalogBindingDirection.Negative:
                        effective = raw < 0f ? -raw : 0f;
                        break;
                    case AnalogBindingDirection.Bipolar:
                    default:
                        effective = raw;
                        break;
                }

                anyValid = true;
                cache[rb.BlendShapeIndex] += effective * rb.Scale;
            }

            if (!anyValid)
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
            // axis 範囲外は早期 false。
            if (axis < 0 || axis >= source.AxisCount)
            {
                value = 0f;
                return false;
            }

            // AxisCount==1 → scalar 経路。
            if (source.AxisCount == 1)
            {
                return source.TryReadScalar(out value);
            }

            // AxisCount==2 → Vector2 経路（boxing/alloc を避ける）。
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

            // N-axis 経路: 軸数分のスタック領域を一時確保して 1 軸だけ抽出する。
            // ARKit 52ch を想定しても 52 * 4B = 208B、安全な stackalloc サイズ。
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
            public readonly int BlendShapeIndex;
            public readonly float Scale;
            public readonly AnalogBindingDirection Direction;

            public IAnalogInputSource Source;

            public ResolvedBinding(
                string sourceId,
                IAnalogInputSource source,
                int sourceAxis,
                int blendShapeIndex,
                float scale,
                AnalogBindingDirection direction)
            {
                SourceId = sourceId;
                OriginalSource = source;
                Source = source;
                SourceAxis = sourceAxis;
                BlendShapeIndex = blendShapeIndex;
                Scale = scale;
                Direction = direction;
            }
        }
    }
}
