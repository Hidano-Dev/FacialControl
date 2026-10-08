using Hidano.FacialControl.Domain.Interfaces;

namespace Hidano.FacialControl.Timeline.Adapters.InputSources
{
    /// <summary>
    /// Timeline が registry の既存エントリを Replace で乗っ取るときに差し込む sink の共通契約（Analog / Gaze / 値提供型）。
    /// </summary>
    /// <remarks>
    /// 乗っ取り前の原本は <see cref="AttachReplacement"/> で退避し、復元時に <see cref="ClearReplacement"/> で忘れる。
    /// 占有規則は <see cref="IInjectedInputSource"/> に従う。
    /// </remarks>
    public interface ITimelineTakeoverSink : IInputSource, IInjectedInputSource
    {
        void AttachReplacement(IInputSource replacedSource);

        void ClearReplacement();

        /// <summary>値を無効にする（Clip の外 / セッション終了時）。</summary>
        void Invalidate();
    }
}
