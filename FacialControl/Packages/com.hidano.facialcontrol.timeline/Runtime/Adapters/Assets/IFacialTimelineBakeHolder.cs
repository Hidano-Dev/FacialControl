namespace Hidano.FacialControl.Timeline.Adapters.Assets
{
    /// <summary>
    /// Facial トラック（<c>FacialExpressionTrack</c> / <c>FacialValueTrack</c>）が保持する Bake 参照の契約（D5）。
    /// 実体は各トラックの <c>[SerializeField, HideInInspector]</c> フィールドで、Exporter / 再ベイクが
    /// 全 Facial トラック（root + 子）へ同じ Bake サブアセット参照を書き、Runtime の Locator が一致を検証する。
    /// </summary>
    public interface IFacialTimelineBakeHolder
    {
        /// <summary>このトラックが参照する Bake。未設定は null。</summary>
        FacialTimelineBakeAsset Bake { get; set; }
    }
}
