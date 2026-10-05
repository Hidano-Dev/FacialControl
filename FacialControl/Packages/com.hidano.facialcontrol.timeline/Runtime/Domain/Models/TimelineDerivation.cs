using System;
using System.Collections.Generic;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// トラック記述子列 + Profile から導出したレイヤー / チャネルの集合。
    /// </summary>
    public sealed class TimelineDerivation
    {
        public TimelineDerivation(
            IReadOnlyList<TimelineLayerDescriptor> layers,
            IReadOnlyList<string> unmatchedTrackNames,
            IReadOnlyList<TimelineChannelDescriptor> channels,
            IReadOnlyList<string> invalidChannelSubIds,
            bool hasFacialTracks)
        {
            Layers = layers ?? Array.Empty<TimelineLayerDescriptor>();
            UnmatchedTrackNames = unmatchedTrackNames ?? Array.Empty<string>();
            Channels = channels ?? Array.Empty<TimelineChannelDescriptor>();
            InvalidChannelSubIds = invalidChannelSubIds ?? Array.Empty<string>();
            HasFacialTracks = hasFacialTracks;
        }

        /// <summary>Profile のレイヤーと一致した root Expression トラック（TrackIndex 昇順）。</summary>
        public IReadOnlyList<TimelineLayerDescriptor> Layers { get; }

        /// <summary>Profile のレイヤーと一致しなかった root Expression トラックの名前（Req 8.2 の診断用）。</summary>
        public IReadOnlyList<string> UnmatchedTrackNames { get; }

        /// <summary>root Value トラックから導出した有効なチャネル。</summary>
        public IReadOnlyList<TimelineChannelDescriptor> Channels { get; }

        /// <summary>重複（後続側）・軸数 0・空の ChannelSubId。</summary>
        public IReadOnlyList<string> InvalidChannelSubIds { get; }

        /// <summary>Facial トラックが 1 本以上あるか。</summary>
        public bool HasFacialTracks { get; }
    }
}
