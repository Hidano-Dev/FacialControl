using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Services;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline の active 表情状態だけを既存入力パイプラインへ供給する trigger sink。
    /// blendShapeCount を 0 に固定し、値出力は構造的に発生させない。
    /// </summary>
    /// <remarks>
    /// <see cref="ContributeMask"/> はホストの BlendShape 数に揃えた全 false の専用 BitArray を返す。
    /// 本 sink がレイヤー入力源として接続された場合でも、<see cref="LayerInputSourceAggregator"/> の
    /// <c>layerMask.Or(source.ContributeMask)</c> が長さ不一致で <see cref="ArgumentException"/> を投げないようにするための多層防御。
    /// </remarks>
    public sealed class TimelineExpressionStateSink : ExpressionTriggerInputSourceBase, ITimelineTriggerSink
    {
        private static readonly string[] EmptyBlendShapeNames = Array.Empty<string>();

        private readonly BitArray _hostSizedEmptyMask;

        /// <param name="id">入力源識別子。</param>
        /// <param name="maxStackDepth">Expression スタックの最大深度。</param>
        /// <param name="exclusionMode">レイヤーの排他モード。</param>
        /// <param name="blendShapeNames">ホスト（SkinnedMeshRenderer 側）の BlendShape 名列。<see cref="ContributeMask"/> の長さにのみ使う。</param>
        /// <param name="profile">Expression 検索に用いるプロファイル。</param>
        /// <exception cref="ArgumentNullException"><paramref name="blendShapeNames"/> が null の場合。</exception>
        public TimelineExpressionStateSink(
            InputSourceId id,
            int maxStackDepth,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile profile)
            : base(
                id,
                blendShapeCount: 0,
                maxStackDepth: maxStackDepth,
                exclusionMode: exclusionMode,
                blendShapeNames: EmptyBlendShapeNames,
                profile: profile)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }

            _hostSizedEmptyMask = new BitArray(blendShapeNames.Count, false);
        }

        /// <summary>
        /// ホストの BlendShape 数に揃えた全 false のマスク。本 sink は値を書かないため寄与 index は常に空。
        /// </summary>
        public override BitArray ContributeMask => _hostSizedEmptyMask;
    }
}
