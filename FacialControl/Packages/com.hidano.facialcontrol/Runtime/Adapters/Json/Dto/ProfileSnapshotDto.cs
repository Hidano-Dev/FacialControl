using System.Collections.Generic;

namespace Hidano.FacialControl.Adapters.Json.Dto
{
    /// <summary>
    /// プロファイル JSON: トップレベル DTO。
    /// <c>schemaVersion / slots[] / layers[] / expressions[] / rendererPaths[] / gazeConfigs[]</c> を保持する。
    /// JsonUtility 互換のため <see cref="System.SerializableAttribute"/> を付与する。
    /// <para>
    /// schemaVersion は <c>"1.0"</c> 以外を許容しない。
    /// パース時の strict チェックは <see cref="SystemTextJsonParser.ParseProfileSnapshotV2(string)"/> が担う。
    /// </para>
    /// </summary>
    [System.Serializable]
    public sealed class ProfileSnapshotDto
    {
        /// <summary>スキーマバージョン文字列（<c>"1.0"</c> 固定）。</summary>
        public string schemaVersion;

        /// <summary>レイヤー定義配列。</summary>
        public List<LayerDefinitionDto> layers;

        /// <summary>プロファイル全体で宣言される overlay slot 識別子配列。</summary>
        public List<string> slots;

        /// <summary>表情エントリ配列（<see cref="ExpressionSnapshotDto"/> を含む snapshot 形式）。</summary>
        public List<ExpressionDto> expressions;

        /// <summary>ベース表情 snapshot。AnimationClip 参照は JSON に含めない。</summary>
        public ExpressionSnapshotDto baseExpression;

        /// <summary>
        /// プロファイル全体で参照される SkinnedMeshRenderer Transform 階層パスの集合。
        /// 各 <see cref="ExpressionSnapshotDto.rendererPaths"/> はこの集合の subset である必要がある。
        /// </summary>
        public List<string> rendererPaths;

        /// <summary>新スキーマの gaze セクション。欠落は Converter 側で既定チャネルに補完する。</summary>
        public GazeSectionDto gaze;

        /// <summary>
        /// active 表情に slot 宣言が無い場合の fallback 用 default overlay 一覧。
        /// </summary>
        public List<OverlaySlotBindingDto> defaultOverlays;
    }
}
