using System;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>AnimationClip へ書き出すときの Clip の分け方（HID-422）。</summary>
    internal enum FacialAnimationClipSplitMode
    {
        /// <summary>BlendShape の担当レイヤーごとの Clip + Base + Gaze（HID-410。既定）。</summary>
        Layer = 0,

        /// <summary>BlendShape 名 1 つにつき Clip 1 本 + Gaze。1 本あたりのアセットを小さくする。</summary>
        BlendShape = 1,
    }

    /// <summary>
    /// AnimationClip 書き出し（AnimationTrack 変換と REC からの直接書き出し）のオプション（HID-422）。
    /// <c>default</c> は従来どおり（レイヤー単位・Timeline のフレームレート）。
    /// </summary>
    internal readonly struct FacialAnimationClipExportOptions : IEquatable<FacialAnimationClipExportOptions>
    {
        /// <param name="splitMode">Clip の分け方。</param>
        /// <param name="frameRate">評価・書き出しのフレームレート。0 以下・非有限なら Timeline のフレームレートに従う。</param>
        public FacialAnimationClipExportOptions(FacialAnimationClipSplitMode splitMode, double frameRate)
        {
            SplitMode = splitMode;
            FrameRate = frameRate;
        }

        public FacialAnimationClipSplitMode SplitMode { get; }

        /// <summary>指定したフレームレート（0 以下・非有限なら指定なし）。</summary>
        public double FrameRate { get; }

        /// <summary>フレームレートを指定しているか。</summary>
        public bool HasFrameRate => FrameRate > 0d && !double.IsInfinity(FrameRate);

        /// <summary>
        /// 使うフレームレート。指定があればそれ、無ければ <paramref name="timelineFrameRate"/>、それも無ければ
        /// <see cref="FacialAnimationTrackConverter.FallbackFrameRate"/>。
        /// </summary>
        public double ResolveFrameRate(double timelineFrameRate)
        {
            if (HasFrameRate)
            {
                return FrameRate;
            }

            return timelineFrameRate > 0d && !double.IsInfinity(timelineFrameRate)
                ? timelineFrameRate
                : FacialAnimationTrackConverter.FallbackFrameRate;
        }

        public bool Equals(FacialAnimationClipExportOptions other)
        {
            return SplitMode == other.SplitMode && FrameRate.Equals(other.FrameRate);
        }

        public override bool Equals(object obj)
        {
            return obj is FacialAnimationClipExportOptions other && Equals(other);
        }

        public override int GetHashCode()
        {
            return ((int)SplitMode * 397) ^ FrameRate.GetHashCode();
        }
    }
}
