using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// Profile の Gaze セクションの <see cref="GazeChannel"/> 設定 (目ボーン path・可動範囲を含む) を
    /// binding へ注入する契約。
    /// </summary>
    /// <remarks>
    /// チャネル id だけを渡す <see cref="Domain.Adapters.IGazeChannelConsumer"/> と同じく、rebuild ごと・
    /// binding の OnStart より前に呼ばれる。引数は null ではない。binding は次の rebuild まで参照を
    /// 保持してよいが、要素を書き換えてはならない。
    /// </remarks>
    public interface IGazeChannelSettingsConsumer
    {
        /// <summary>rebuild ごとに、OnStart 前の binding へ Gaze チャネル設定を注入します。</summary>
        void ConfigureGazeChannelSettings(IReadOnlyList<GazeChannel> channels);
    }
}
