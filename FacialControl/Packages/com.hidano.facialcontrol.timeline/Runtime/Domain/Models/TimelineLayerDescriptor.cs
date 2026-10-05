namespace Hidano.FacialControl.Timeline.Domain.Models
{
    /// <summary>
    /// root の Expression トラックから導出したレイヤー。Track 参照は持たず <see cref="TrackIndex"/> で逆引きする（D14）。
    /// </summary>
    public readonly struct TimelineLayerDescriptor
    {
        public TimelineLayerDescriptor(string layerName, int layerIndex, int trackIndex)
        {
            LayerName = layerName ?? string.Empty;
            LayerIndex = layerIndex;
            TrackIndex = trackIndex;
        }

        /// <summary>root Expression トラックの名前（= Profile のレイヤー名）。</summary>
        public string LayerName { get; }

        /// <summary>Profile のレイヤー index。未一致は -1。</summary>
        public int LayerIndex { get; }

        /// <summary>対応する <see cref="TimelineTrackDescriptor.TrackIndex"/>。</summary>
        public int TrackIndex { get; }

        public bool IsMatched => LayerIndex >= 0;
    }
}
