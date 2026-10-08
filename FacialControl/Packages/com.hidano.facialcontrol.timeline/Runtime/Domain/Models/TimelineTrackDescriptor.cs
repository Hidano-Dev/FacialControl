using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Tracks;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// Facial トラックの種別。
    /// </summary>
    public enum TimelineTrackKind
    {
        Expression = 0,
        Value = 1,
    }

    /// <summary>
    /// TimelineAsset を走査した結果の Facial トラック 1 本分の記述子。Unity 型を含まない（D14）。
    /// Adapters の Scanner が生成し、<see cref="TrackIndex"/> で同じ走査結果の TrackAsset 列へ逆引きする。
    /// </summary>
    public readonly struct TimelineTrackDescriptor
    {
        public TimelineTrackDescriptor(
            int trackIndex,
            TimelineTrackKind kind,
            string name,
            string channelSubId,
            FacialValueChannelKind channelKind,
            int maxAxisCount,
            bool hasBakeReference,
            int bakeInstanceId,
            bool isChild,
            int parentIndex,
            IReadOnlyList<TimelineBlendShapeBinding> blendShapeBindings = null)
        {
            BlendShapeBindings = blendShapeBindings ?? Array.Empty<TimelineBlendShapeBinding>();
            TrackIndex = trackIndex;
            Kind = kind;
            Name = name ?? string.Empty;
            ChannelSubId = channelSubId ?? string.Empty;
            ChannelKind = channelKind;
            MaxAxisCount = maxAxisCount;
            HasBakeReference = hasBakeReference;
            BakeInstanceId = bakeInstanceId;
            IsChild = isChild;
            ParentIndex = parentIndex;
        }

        /// <summary>走査結果内の index（Adapters が TrackAsset へ逆引きする鍵）。</summary>
        public int TrackIndex { get; }

        public TimelineTrackKind Kind { get; }

        /// <summary>トラック名（TrackAsset.name）。</summary>
        public string Name { get; }

        /// <summary>Value トラックのみ。REC の source id をそのまま保持する。</summary>
        public string ChannelSubId { get; }

        /// <summary>Value トラックのみ。</summary>
        public FacialValueChannelKind ChannelKind { get; }

        /// <summary>Value トラックのみ。クリップの軸数の最大値（0 なら無効）。</summary>
        public int MaxAxisCount { get; }

        /// <summary>トラックが Bake 参照を保持しているか。</summary>
        public bool HasBakeReference { get; }

        /// <summary>Bake 参照の同一性比較用キー（参照なしは 0）。</summary>
        public int BakeInstanceId { get; }

        /// <summary>子トラック（<c>{layer} Lane n</c>）か。</summary>
        public bool IsChild { get; }

        /// <summary>子トラックのとき親の <see cref="TrackIndex"/>、root は -1。</summary>
        public int ParentIndex { get; }

        /// <summary>値提供型 Value トラックのみ。Clip の軸の BlendShape 対応（全 Clip の和集合、初出順）。それ以外は空。</summary>
        public IReadOnlyList<TimelineBlendShapeBinding> BlendShapeBindings { get; }
    }
}
