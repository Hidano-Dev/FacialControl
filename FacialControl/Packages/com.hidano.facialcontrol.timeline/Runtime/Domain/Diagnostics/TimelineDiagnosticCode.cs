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
        ProfileBinding,
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

        // TrackBinding
        TrackBindingAutoAssigned,
        TrackBindingForeign,

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

        // ProfileBinding
        BindingMissing,
        BindingDisabled,
        BindingLegacyFields,
        BindingSlugInvalid,

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
        ReceiverNotOnControllerObject,
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
    }
}
