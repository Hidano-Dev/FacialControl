namespace Hidano.FacialControl.Timeline.Domain.Diagnostics
{
    /// <summary>
    /// 診断の重大度。<see cref="FacialTimelineDiagnostics.Overall"/> は最大値で求める。
    /// </summary>
    public enum TimelineDiagnosticSeverity
    {
        Info = 0,
        Ok = 1,
        Warning = 2,
        Error = 3,
    }

    /// <summary>
    /// 診断の領域。<see cref="FacialTimelineDiagnostics"/> は領域単位で項目を置換する。
    /// </summary>
    public enum TimelineDiagnosticArea
    {
        Director,
        TrackBinding,
        Bake,
        Profile,
        LayerMatch,
        LayerConnection,
        Analog,
        Gaze,
        ValueProvider,
        Placement,
        Session,
    }

    /// <summary>
    /// 診断コード。テストと Inspector はログ文言ではなくこの値で診断を判定する。
    /// </summary>
    public enum TimelineDiagnosticCode
    {
        Ok,

        // Director
        DirectorMissing,
        TimelineNotBound,
        DirectorAmbiguous,

        // TrackBinding（トラック構造。HID-408）

        /// <summary>Facial Character トラックの外に Facial トラックがある（親トラック導入前の旧構造）。REC から書き出し直す。</summary>
        LegacyTrackStructure,

        /// <summary>1 つの Timeline に Facial Character トラックが複数ある（合成は Timeline 全体を 1 体として評価するため未対応）。</summary>
        MultipleCharacterTracks,

        // Bake
        BakeFresh,
        BakeMissing,
        BakeStale,
        BakeLegacyExport,
        BakeReferenceConflict,
        BakeOverrideUsed,
        BakeOverrideDiffers,
        UnsavedTimeline,

        // Profile
        ProfileMatched,
        ProfileMismatch,

        // LayerMatch
        TrackLayerUnmatched,
        LayerSinkIdFallback,

        // LayerConnection
        LayerConnected,
        LayerConnectionSkippedDeclared,
        LayerConnectionFailed,
        LegacyStateDeclaration,

        // Analog
        AnalogTakeoverAttached,
        AnalogSourceNotFound,
        AnalogOccupied,
        AnalogAxisCountInvalid,

        // Gaze
        GazeTakeoverAttached,
        GazeSourceNotFound,
        GazeOccupied,

        // Placement
        ControllerMissing,
        ControllerNotInitialized,

        // Session
        SessionConflict,

        // ValueProvider
        ValueProviderTakeoverAttached,
        ValueProviderSourceNotFound,
        ValueProviderOccupied,

        /// <summary>Clip の BlendShape の一部がホストの BlendShape に対応しない（名前が無い / index が範囲外）。対応した分だけ再生する。</summary>
        ValueProviderBlendShapeMismatch,

        /// <summary>
        /// 値提供型の入力源 id が Profile のどのレイヤーにも宣言されていない。乗っ取りはレイヤーの宣言スロットにしか届かないため
        /// （実行時に後付け接続されたスロットは registry の置換を購読しない）、Timeline の値は合成されない。
        /// </summary>
        ValueProviderNotDeclared,
    }
}
