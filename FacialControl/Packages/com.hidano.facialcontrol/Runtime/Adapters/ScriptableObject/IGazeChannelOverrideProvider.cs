namespace Hidano.FacialControl.Adapters.ScriptableObject
{
    /// <summary>
    /// <see cref="GazeChannel"/> の目ボーン path・可動範囲を外部から上書きする binding の契約。
    /// </summary>
    /// <remarks>
    /// <para>
    /// FacialController は rebuild ごとにこの契約を実装する binding を集め、毎フレーム
    /// <see cref="GazeChannelOverrideVersion"/> だけを読む。値が変わったときに限り
    /// <see cref="TryGetGazeChannelOverride"/> で上書きを取り直し、目ボーン provider を再構築する
    /// (毎フレームのヒープ確保・path 解決をしない)。
    /// </para>
    /// <para>
    /// 両メンバーはメインスレッドから呼ばれる。実装は上書きの内容が変わったときだけ
    /// <see cref="GazeChannelOverrideVersion"/> を進めること。
    /// </para>
    /// </remarks>
    public interface IGazeChannelOverrideProvider
    {
        /// <summary>上書きの内容が変わるたびに進む値。</summary>
        int GazeChannelOverrideVersion { get; }

        /// <summary>チャネル id に対する上書きを返す。上書きが無ければ false。</summary>
        bool TryGetGazeChannelOverride(string channelId, out GazeChannelOverride value);
    }
}
