namespace Hidano.FacialControl.Timeline.Tracks
{
    public enum FacialValueChannelKind
    {
        Analog = 0,
        Gaze = 1,

        /// <summary>
        /// 値提供型（BlendShape ごとの疎な値。REC の kind 7 / 8）。Clip の軸 = BlendShape で、値・寄与 mask・有効状態を持つ。
        /// </summary>
        ValueProvider = 2,
    }
}
