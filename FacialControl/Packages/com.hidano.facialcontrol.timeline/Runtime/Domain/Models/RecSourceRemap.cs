using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// REC の入力源 id / レイヤー名を、再生するキャラの Profile の入力源 id / レイヤー名へ付け替える対応表（HID-412）。
    /// </summary>
    /// <remarks>
    /// <para>REC を録ったキャラと入力源・レイヤーの構成が違うキャラ（例: 送信側の <c>ifm</c> / <c>udp-lipsync</c> で録った REC を
    /// <c>osc-receiver</c> だけのキャラで再生する）へ Export するときに使う。<c>.fcrec</c> は変えず、Export の入力として渡す。</para>
    /// <para>対応表に無い id はそのまま使う（既定は「同じ id へ」）。対応先を <c>null</c> / 空文字にした id は除外する
    /// （その入力源・レイヤーの記録を書き出さない）。</para>
    /// <para>入力源の付け替え先のレイヤーは、対象 Profile でその入力源 id を宣言しているレイヤー（<c>Layer.inputSources</c>）で決まる。
    /// レイヤーの対応表はレイヤー weight の記録（レイヤー名で記録される）に使う。BlendShape は名前で対応付くため対応表を持たない。</para>
    /// </remarks>
    public sealed class RecSourceRemap
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyMap =
            new Dictionary<string, string>(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _sourceMap;
        private readonly Dictionary<string, string> _layerMap;

        /// <summary>何も付け替えない対応表。</summary>
        public static RecSourceRemap Identity { get; } = new RecSourceRemap(null, null);

        /// <param name="sourceMap">REC の入力源 id → 対象の入力源 id。値が null / 空なら除外。</param>
        /// <param name="layerMap">REC のレイヤー名 → 対象のレイヤー名。値が null / 空なら除外。</param>
        public RecSourceRemap(
            IEnumerable<KeyValuePair<string, string>> sourceMap,
            IEnumerable<KeyValuePair<string, string>> layerMap)
        {
            _sourceMap = Copy(sourceMap, nameof(sourceMap));
            _layerMap = Copy(layerMap, nameof(layerMap));
        }

        /// <summary>入力源の対応表（REC の id → 対象の id。除外は空文字）。</summary>
        public IReadOnlyDictionary<string, string> SourceMap => _sourceMap.Count == 0 ? EmptyMap : _sourceMap;

        /// <summary>レイヤーの対応表（REC のレイヤー名 → 対象のレイヤー名。除外は空文字）。</summary>
        public IReadOnlyDictionary<string, string> LayerMap => _layerMap.Count == 0 ? EmptyMap : _layerMap;

        /// <summary>付け替えを何もしない（対応表が空、またはすべて同じ id へ対応する）か。</summary>
        public bool IsIdentity => IsIdentityMap(_sourceMap) && IsIdentityMap(_layerMap);

        /// <summary>
        /// REC の入力源 id の付け替え先を返す。対応表に無ければ同じ id。除外指定なら false。
        /// </summary>
        public bool TryMapSource(string recordedSourceId, out string targetSourceId)
        {
            return TryMap(_sourceMap, recordedSourceId, out targetSourceId);
        }

        /// <summary>
        /// REC のレイヤー名の付け替え先を返す。対応表に無ければ同じ名前。除外指定なら false。
        /// </summary>
        public bool TryMapLayer(string recordedLayerName, out string targetLayerName)
        {
            return TryMap(_layerMap, recordedLayerName, out targetLayerName);
        }

        private static bool TryMap(Dictionary<string, string> map, string recordedId, out string targetId)
        {
            if (recordedId != null && map.TryGetValue(recordedId, out string mapped))
            {
                targetId = mapped;
                return mapped.Length > 0;
            }

            targetId = recordedId;
            return true;
        }

        private static bool IsIdentityMap(Dictionary<string, string> map)
        {
            foreach (KeyValuePair<string, string> pair in map)
            {
                if (!string.Equals(pair.Key, pair.Value, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return true;
        }

        private static Dictionary<string, string> Copy(IEnumerable<KeyValuePair<string, string>> pairs, string paramName)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (pairs == null)
            {
                return map;
            }

            foreach (KeyValuePair<string, string> pair in pairs)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    throw new ArgumentException("Recorded ids must be non-empty.", paramName);
                }

                if (map.ContainsKey(pair.Key))
                {
                    throw new ArgumentException($"Duplicate recorded id '{pair.Key}' is not allowed.", paramName);
                }

                map.Add(pair.Key, string.IsNullOrWhiteSpace(pair.Value) ? string.Empty : pair.Value);
            }

            return map;
        }
    }
}
