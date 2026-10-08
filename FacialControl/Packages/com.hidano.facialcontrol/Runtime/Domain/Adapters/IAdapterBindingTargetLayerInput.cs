namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// binding が登録する入力源を、起動時に既存レイヤーの <c>inputSources</c> 宣言へ自動で補うことを表す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="IAdapterBindingDefaultLayer"/> が Editor で binding 追加直後に専用 Layer を作る hook なのに対し、
    /// 本 interface はランタイムの解決結果だけに効く（Profile アセットは書き換えない）。
    /// 補い方は <see cref="Hidano.FacialControl.Domain.Services.TargetLayerInputSourceResolver"/> が決める。
    /// </para>
    /// <para>
    /// OSC 受信のように「既存レイヤーへ値を足す」binding が実装する。
    /// </para>
    /// </remarks>
    public interface IAdapterBindingTargetLayerInput
    {
        /// <summary>
        /// 入力源を足す既存レイヤーの名前。null / 空白なら未指定として扱う（プロファイルの先頭レイヤー）。
        /// </summary>
        string TargetLayerName { get; }

        /// <summary>
        /// ランタイムで補う入力源 id。通常は binding の <see cref="AdapterBindingBase.Slug"/>。null / 空白なら何も補わない
        /// （起動に失敗した binding は null を返して、解決できない宣言を補わない）。
        /// </summary>
        string TargetLayerInputSourceId { get; }

        /// <summary>
        /// 起動状態に依存しない、設定上の補う入力源 id（通常は binding の <see cref="AdapterBindingBase.Slug"/>）。
        /// Editor のルーティング表示など、binding を起動しない経路が自動宣言を求めるときに使う。null / 空白なら何も補わない。
        /// </summary>
        string ConfiguredTargetLayerInputSourceId { get; }
    }
}
