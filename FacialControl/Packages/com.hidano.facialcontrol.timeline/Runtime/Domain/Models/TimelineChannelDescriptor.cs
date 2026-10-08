using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Tracks;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// root の Value トラックから導出したチャネル。Track 参照は持たず <see cref="TrackIndex"/> で逆引きする（D14）。
    /// </summary>
    public readonly struct TimelineChannelDescriptor
    {
        public TimelineChannelDescriptor(
            string channelSubId,
            FacialValueChannelKind kind,
            int axisCount,
            int trackIndex,
            IReadOnlyList<TimelineBlendShapeBinding> blendShapeBindings = null)
        {
            ChannelSubId = channelSubId ?? string.Empty;
            Kind = kind;
            AxisCount = axisCount;
            TrackIndex = trackIndex;
            BlendShapeBindings = blendShapeBindings ?? Array.Empty<TimelineBlendShapeBinding>();
        }

        /// <summary>REC の source id（<c>slug:sub</c>）をそのまま保持する。</summary>
        public string ChannelSubId { get; }

        public FacialValueChannelKind Kind { get; }

        /// <summary>クリップの軸数の最大値。</summary>
        public int AxisCount { get; }

        /// <summary>対応する <see cref="TimelineTrackDescriptor.TrackIndex"/>。</summary>
        public int TrackIndex { get; }

        /// <summary>値提供型のみ。軸の BlendShape 対応（全 Clip の和集合）。それ以外は空。</summary>
        public IReadOnlyList<TimelineBlendShapeBinding> BlendShapeBindings { get; }
    }
}
