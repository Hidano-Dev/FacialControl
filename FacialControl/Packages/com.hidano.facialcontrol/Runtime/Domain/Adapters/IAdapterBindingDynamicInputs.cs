namespace Hidano.FacialControl.Domain.Adapters
{
    /// <summary>
    /// <c>{Slug}:*</c> 形式の入力源 id を実行時に導出して registry へ登録する binding を示すマーカー interface。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 静的に列挙できる入力源ポートを持たず、Timeline のレイヤー / チャネル導出のように
    /// 実行時にしか id が決まらない binding が実装する。メンバは持たない。
    /// </para>
    /// <para>
    /// Routing 検証（<c>InvalidIdValidator</c>）は、本 interface を実装する binding の
    /// <c>{Slug}:</c> prefix に一致する宣言 id を不正扱いしない。
    /// <see cref="IAdapterBindingDefaultLayerInputs"/> とは異なり、binding 追加時の
    /// inputSources 自動追加や AutoWire の対象にはならない。
    /// </para>
    /// </remarks>
    public interface IAdapterBindingDynamicInputs
    {
    }
}
