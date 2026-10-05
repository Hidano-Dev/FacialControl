using Hidano.FacialControl.Timeline.Tracks;

namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// root の Value トラックから導出したチャネル。Track 参照は持たず <see cref="TrackIndex"/> で逆引きする（D14）。
    /// </summary>
    public readonly struct TimelineChannelDescriptor
    {
        public TimelineChannelDescriptor(string channelSubId, FacialValueChannelKind kind, int axisCount, int trackIndex)
        {
            ChannelSubId = channelSubId ?? string.Empty;
            Kind = kind;
            AxisCount = axisCount;
            TrackIndex = trackIndex;
        }

        /// <summary>REC の source id（<c>slug:sub</c>）をそのまま保持する。</summary>
        public string ChannelSubId { get; }

        public FacialValueChannelKind Kind { get; }

        /// <summary>クリップの軸数の最大値。</summary>
        public int AxisCount { get; }

        /// <summary>対応する <see cref="TimelineTrackDescriptor.TrackIndex"/>。</summary>
        public int TrackIndex { get; }
    }
}
