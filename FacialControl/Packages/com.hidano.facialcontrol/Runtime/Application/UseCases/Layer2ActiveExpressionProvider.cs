using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Application.UseCases
{
    /// <summary>
    /// 系2（<see cref="ExpressionTriggerInputSourceBase"/> 群）から
    /// 「指定レイヤーで現在 active な top Expression」を解決する
    /// <see cref="IActiveExpressionProvider"/> 実装。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 背景: 実機の InputSystem 表情トリガーは <see cref="ExpressionTriggerInputSourceBase"/>（系2, input source）
    /// に表情を積むが、従来 <c>OverlayInputSource</c> / layerOverrideMask が参照する
    /// <see cref="ExpressionUseCase"/>（系1）には登録されない。そのため active provider が常に空となり
    /// overlay suppress が実機で機能しなかった。本クラスは系2 の
    /// <see cref="ExpressionTriggerInputSourceBase.ActiveExpressionIds"/> を読み、実機経路の active 表情を提供する。
    /// </para>
    /// <para>
    /// build-order（後期バインド）: <c>OverlayInputSource</c> はレイヤー入力源（系2）が解決される前に
    /// 構築されるため、本 provider は空で生成して先に注入し、系2 解決後に
    /// <see cref="SetSources"/> で実体（(layer, source) 群）を後から流し込む。
    /// 宣言の無い系2 入力源（Timeline の state sink 等）を実行中に増減させる場合は
    /// <see cref="AddSource"/> / <see cref="RemoveSource"/> を使う（一括設定済みのソースは保持される）。
    /// </para>
    /// <para>
    /// 「top」の暫定定義: 指定レイヤーに紐づく系2 インスタンスを走査し、最後に観測した非空スタックの末尾
    /// （= 最新 TriggerOn）を返す。同一レイヤーに複数の系2 インスタンス（device 別 sink 等）が
    /// 同時に異なる表情を積んだ場合の確定的な順序・合成ルールは別途詰める（design-notes 参照）。
    /// </para>
    /// </remarks>
    public sealed class Layer2ActiveExpressionProvider : IActiveExpressionProvider
    {
        private readonly FacialProfile _profile;
        private readonly List<LayerSource> _sources = new List<LayerSource>();

        /// <summary>
        /// provider を生成する。生成直後はソース未設定（常に null を返す）で、
        /// <see cref="SetSources"/> 後に解決可能になる。
        /// </summary>
        /// <param name="profile">id → <see cref="Expression"/> 解決に用いる profile。</param>
        public Layer2ActiveExpressionProvider(FacialProfile profile)
        {
            _profile = profile;
        }

        /// <summary>
        /// 系2 ソース群（レイヤー名付き）を後から流し込む。既存のソースは置き換えられる。
        /// </summary>
        /// <param name="sources">(レイヤー名, 系2 入力源) の列。null は空として扱う。</param>
        public void SetSources(IEnumerable<(string layer, ExpressionTriggerInputSourceBase source)> sources)
        {
            _sources.Clear();
            if (sources == null)
            {
                return;
            }

            foreach (var (layer, source) in sources)
            {
                if (source == null || string.IsNullOrEmpty(layer))
                {
                    continue;
                }
                _sources.Add(new LayerSource(layer, source));
            }
        }

        /// <summary>
        /// 単一の系2 ソースをレイヤー名付きで追加する。<see cref="SetSources"/> で流し込んだ既存ソースは保持する。
        /// 同じ (layer, source) の組が既に登録されていれば重複追加しない。
        /// null の source・null / 空のレイヤー名は無視する。
        /// </summary>
        /// <param name="layer">対象レイヤー名。</param>
        /// <param name="source">追加する系2 入力源。</param>
        public void AddSource(string layer, ExpressionTriggerInputSourceBase source)
        {
            if (source == null || string.IsNullOrEmpty(layer))
            {
                return;
            }

            if (IndexOf(layer, source) >= 0)
            {
                return;
            }

            _sources.Add(new LayerSource(layer, source));
        }

        /// <summary>
        /// <see cref="AddSource"/> / <see cref="SetSources"/> で登録した (layer, source) の組を取り除く。
        /// </summary>
        /// <param name="layer">対象レイヤー名。</param>
        /// <param name="source">取り除く系2 入力源。</param>
        /// <returns>取り除いた場合 true。未登録の組・null は false。</returns>
        public bool RemoveSource(string layer, ExpressionTriggerInputSourceBase source)
        {
            if (source == null || string.IsNullOrEmpty(layer))
            {
                return false;
            }

            int index = IndexOf(layer, source);
            if (index < 0)
            {
                return false;
            }

            _sources.RemoveAt(index);
            return true;
        }

        private int IndexOf(string layer, ExpressionTriggerInputSourceBase source)
        {
            for (int i = 0; i < _sources.Count; i++)
            {
                var entry = _sources[i];
                if (ReferenceEquals(entry.Source, source)
                    && string.Equals(entry.Layer, layer, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <inheritdoc />
        public Expression? TryGetTopActiveExpression(string layerName)
        {
            if (string.IsNullOrEmpty(layerName) || _sources.Count == 0)
            {
                return null;
            }

            string topId = null;
            for (int i = 0; i < _sources.Count; i++)
            {
                var entry = _sources[i];
                if (!string.Equals(entry.Layer, layerName, StringComparison.Ordinal))
                {
                    continue;
                }

                var ids = entry.Source.ActiveExpressionIds;
                if (ids != null && ids.Count > 0)
                {
                    // スタック末尾 = 最新 TriggerOn = top。
                    topId = ids[ids.Count - 1];
                }
            }

            if (topId == null)
            {
                return null;
            }

            return _profile.FindExpressionById(topId);
        }

        private readonly struct LayerSource
        {
            public readonly string Layer;
            public readonly ExpressionTriggerInputSourceBase Source;

            public LayerSource(string layer, ExpressionTriggerInputSourceBase source)
            {
                Layer = layer;
                Source = source;
            }
        }
    }
}
