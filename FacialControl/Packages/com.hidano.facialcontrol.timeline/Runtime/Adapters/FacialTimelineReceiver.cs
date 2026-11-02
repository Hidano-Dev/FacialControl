using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Diagnostics;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Adapters
{
    /// <summary>
    /// Receiver の再生セッション状態。
    /// </summary>
    public enum TimelineSessionState
    {
        /// <summary>セッション無し（初期状態 / ReleaseAll 後）。</summary>
        Idle = 0,

        /// <summary>FacialController が未初期化のため開始を保留中（ログ無しで毎フレーム再試行する）。</summary>
        Pending = 1,

        /// <summary>レイヤー接続と乗っ取りを済ませて再生中。</summary>
        Active = 2,

        /// <summary>再生できない原因が確定した（ReleaseAll まで再試行しない）。</summary>
        Failed = 3,
    }

    /// <summary>
    /// Timeline 再生の設定・状態・診断の集約点（ファサード）。
    /// </summary>
    /// <remarks>
    /// <para>binding（<c>TimelineAdapterBinding.OnStart</c>）から接続コンテキストを受け取り、Mixer が呼ぶ
    /// <see cref="BeginPlaybackSession"/> で Bake 解決 → レイヤー導出 → 接続（<see cref="TimelineLayerConnector"/>）→
    /// Analog / Gaze の乗っ取り（<see cref="TimelineChannelTakeover"/>）を行う。</para>
    /// <para>Failed の判定順: (1) binding 未接続 / 無効 / Receiver が controller と別 GameObject → (2) Bake 参照の不整合 / 旧形式 →
    /// (3) 旧 <c>:state</c> 宣言。最初に見つかった Error だけを Console に 1 回出す（Inspector は診断状態の全件を読む）。
    /// Profile 内容ハッシュ不一致（ProfileMismatch）と Source ハッシュ不一致（BakeStale）は Warning で、Active のまま再生する。</para>
    /// <para>Play 中の表情の値は Bake のカーブではなく、セッションの Profile とランタイムの trigger 入力源と同じ遷移計算で作る
    /// （<see cref="TryCreateExpressionTransitionPlayer"/>。REC 再生と同じ位相にするため。HID-318）。Bake は Edit プレビューと、
    /// 値 sink の BlendShape 名（レイヤーへの寄与範囲）に使う。</para>
    /// <para>Profile は controller が保持する値（<see cref="FacialController.CurrentProfile"/>）を使い、再読込しない。</para>
    /// <para>(Timeline, Bake, Profile) が同じ間はセッション資源（導出結果・sink・Bake → sink のバインディング）を再利用する。
    /// 同じ Bake インスタンスでも再ベイクで中身（ExpressionBakes / SourceHashHex）が変わったら作り直す。</para>
    /// <para>メインスレッド専用。Active 中の Mixer 向け API（TryGet* / <see cref="ApplyExpressionValues"/>）はヒープ確保しない。</para>
    /// </remarks>
    public sealed class FacialTimelineReceiver : MonoBehaviour
    {
        /// <summary>
        /// Editor の再ベイク修復向けの旧通知（Profile / Source ハッシュ不一致・Bake 無し）。Editor 購読の一元化（9.x）で置き換える。
        /// </summary>
        public static event Action<BakeInspectionIssue> BakeIssueDetected;

        private const string LogPrefix = "[FacialTimelineReceiver] ";
        private const string BindingMissingDetail =
            "Profile の AdapterBindings に Timeline binding が無いため、Timeline の値を受信できません。Profile Inspector で Timeline binding を追加してください。";
        private const string BindingDisabledDetail =
            "Timeline binding の受信が無効になっています。Profile Inspector で Timeline binding を有効にしてください。";
        private const string ReceiverNotOnControllerDetail =
            "FacialTimelineReceiver を FacialController と同じ GameObject に置いてください。";
        private const string ControllerMissingDetail =
            "FacialController が見つかりません。FacialTimelineReceiver と同じ GameObject に FacialController を置いてください。";
        private const string TimelineNotBoundDetail =
            "PlayableDirector に TimelineAsset がセットされていません。Director の Playable 欄に Export した TimelineAsset をセットしてください。";
        private const string SessionConflictDetail =
            "この Receiver は別の PlayableDirector / TimelineAsset で再生中です。1 つの Receiver で同時に再生できるのは 1 つの Director だけです。";

        private static readonly string[] EmptyNames = Array.Empty<string>();
        private static readonly TimelineTakeoverEntry[] EmptyTakeoverEntries = Array.Empty<TimelineTakeoverEntry>();
        private static readonly TimelineDiagnosticItem[] EmptyItems = Array.Empty<TimelineDiagnosticItem>();

        [SerializeField] private FacialTimelineBakeAsset bakeAsset;
        [SerializeField] private PlayableDirector director;

        private readonly FacialTimelineDiagnostics _diagnostics = new FacialTimelineDiagnostics();
        private readonly TimelineOnceWarningGate _warningGate = new TimelineOnceWarningGate();
        private readonly Dictionary<string, ExpressionValuePlayback> _playbackByLayer =
            new Dictionary<string, ExpressionValuePlayback>(StringComparer.Ordinal);

        private TimelineBindingContext _binding;
        private bool _hasBinding;
        private TimelineLayerConnector _connector;
        private TimelineChannelTakeover _takeover;
        private readonly TimelineLayerWeightOverride _layerWeightOverride = new TimelineLayerWeightOverride();

        private TimelineSessionState _state = TimelineSessionState.Idle;
        private TimelineAsset _activeTimeline;
        private PlayableDirector _activeDirector;
        private BakeLocateResult _lastBakeLocate;
        private FacialTimelineBakeAsset _sessionBake;

        // セッション開始時の Profile（表情の値の再生器が Expression と遷移を引く）。
        private FacialProfile _sessionProfile;

        // Play の OnEnable で行った Track binding 自動設定の結果（Start の静的診断の TrackBinding 領域に使う）。
        private TrackBindingReport _trackBindingReport;
        private PlayableDirector _trackBindingDirector;
        private bool _hasTrackBindingReport;

        // 記録済みの SessionConflict の相手（同じ相手の再記録で文字列を作らないため）。
        private PlayableDirector _conflictDirector;
        private bool _hasConflictRecord;

        // セッション資源プールのキー（(Timeline, Bake, Profile) が同じ間は導出結果と Bake → sink のバインディングを再利用する）。
        private TimelineAsset _pooledTimeline;
        private FacialTimelineBakeAsset _pooledBake;
        // 同じ Bake インスタンスでも再ベイク（UpdateBakeAsset）で中身が差し替わるため、中身の同一性もキーに含める。
        private ExpressionSourceBake[] _pooledBakeExpressionBakes;
        private string _pooledBakeSourceHash;
        private FacialProfile _pooledProfile;
        private bool _hasPooledProfile;
        private TimelineDerivation _pooledDerivation;

        /// <summary>任意上書きの Bake（未指定ならトラックの Bake 参照を使う）。</summary>
        public FacialTimelineBakeAsset BakeAsset
        {
            get => bakeAsset;
            set => bakeAsset = value;
        }

        /// <summary>任意上書きの Director（Director 解決順の最優先）。</summary>
        public PlayableDirector DirectorOverride
        {
            get => director;
            set => director = value;
        }

        /// <summary>診断状態（Inspector / テストはログ文言ではなくこの値を読む）。</summary>
        public FacialTimelineDiagnostics Diagnostics => _diagnostics;

        public TimelineSessionState SessionState => _state;

        /// <summary>Active なセッションの TimelineAsset（Active 以外は null）。</summary>
        public TimelineAsset ActiveTimeline => _activeTimeline;

        /// <summary>Active なセッションの Director（Active 以外は null）。</summary>
        public PlayableDirector ActiveDirector => _activeDirector;

        /// <summary>直近のセッション開始で解決した Bake の結果。</summary>
        public BakeLocateResult LastBakeLocate => _lastBakeLocate;

        /// <summary>Analog / Gaze / 値提供型の乗っ取りエントリ（Inspector 表示用）。</summary>
        public IReadOnlyList<TimelineTakeoverEntry> TakeoverEntries =>
            _takeover != null ? _takeover.Entries : EmptyTakeoverEntries;

        /// <summary>値 sink を接続したレイヤー名。</summary>
        public IReadOnlyList<string> ConnectedLayerNames =>
            _connector != null ? _connector.ConnectedLayerNames : EmptyNames;

        /// <summary>binding から接続コンテキストを受け取っているか。</summary>
        public bool IsBindingAttached => _hasBinding;

        /// <summary>受け取った接続コンテキスト（未接続なら既定値）。</summary>
        internal TimelineBindingContext BindingContext => _binding;

        /// <summary>旧ベイク検査の結果（Editor の再ベイク修復向け。Editor 購読の一元化（9.x）で撤去予定）。</summary>
        public BakeInspectionStatus LastBakeInspectionStatus { get; private set; } = BakeInspectionStatus.NotChecked;

        /// <summary>
        /// binding の OnStart から接続コンテキストを受け取る。controller は未初期化でよく、接続はセッション開始まで遅らせる。
        /// 既に接続していれば先に解放する。
        /// </summary>
        internal void AttachBinding(in TimelineBindingContext context)
        {
            DetachBinding();
            _binding = context;
            _hasBinding = true;
        }

        /// <summary>
        /// binding の Dispose から呼ぶ。セッションを解放し、接続コンテキストと接続 / 乗っ取りの資源を破棄する。二重呼び出しは no-op。
        /// </summary>
        internal void DetachBinding()
        {
            ReleaseAll();
            _connector?.Dispose();
            _connector = null;
            _takeover?.Dispose();
            _takeover = null;
            _binding = default;
            _hasBinding = false;
            ClearPool();
        }

        /// <summary>
        /// 再生セッションを開始する。冪等（Active 中は何もしない）。Mixer が毎フレーム呼ぶ。
        /// </summary>
        public void BeginPlaybackSession(TimelineAsset timeline, PlayableDirector playingDirector)
        {
            switch (_state)
            {
                case TimelineSessionState.Active:
                    if (!ReferenceEquals(timeline, _activeTimeline) || !ReferenceEquals(playingDirector, _activeDirector))
                    {
                        RecordSessionConflict(playingDirector);
                    }

                    return;
                case TimelineSessionState.Failed:
                    return;
            }

            // (1) binding・配置
            if (!_hasBinding)
            {
                FacialController hostController = GetComponent<FacialController>();
                if (hostController == null)
                {
                    Fail(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ReceiverNotOnControllerObject, name, ReceiverNotOnControllerDetail);
                }
                else if (!hostController.IsInitialized)
                {
                    // binding の OnStart は controller の初期化中に呼ばれるため、未初期化の間は binding 未接続を確定しない（ログ無し）。
                    _state = TimelineSessionState.Pending;
                }
                else
                {
                    // ヘッダーのトグルで binding ごと無効にした場合は OnStart されず接続も無いが、binding 自体はあるので
                    // 静的診断と同じ BindingDisabled（件名は slug）にする。
                    TimelineAdapterBinding disabledBinding =
                        TimelineDiagnosticsEvaluator.FindTimelineBinding(hostController.CharacterSO);
                    if (disabledBinding != null && disabledBinding.Disabled)
                    {
                        string slugText = string.IsNullOrWhiteSpace(disabledBinding.Slug) ? "timeline" : disabledBinding.Slug;
                        string subject = AdapterSlug.TryParse(slugText, out AdapterSlug disabledSlug) ? disabledSlug.Value : slugText;
                        Fail(TimelineDiagnosticArea.ProfileBinding, TimelineDiagnosticCode.BindingDisabled, subject, BindingDisabledDetail);
                    }
                    else
                    {
                        Fail(TimelineDiagnosticArea.ProfileBinding, TimelineDiagnosticCode.BindingMissing, name, BindingMissingDetail);
                    }
                }

                return;
            }

            if (!_binding.Enabled)
            {
                Fail(TimelineDiagnosticArea.ProfileBinding, TimelineDiagnosticCode.BindingDisabled, _binding.Slug.Value, BindingDisabledDetail);
                return;
            }

            FacialController controller = _binding.Controller;
            if (controller == null)
            {
                Fail(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ControllerMissing, name, ControllerMissingDetail);
                return;
            }

            if (controller.gameObject != gameObject)
            {
                Fail(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ReceiverNotOnControllerObject, name, ReceiverNotOnControllerDetail);
                return;
            }

            if (!controller.IsInitialized || !controller.CurrentProfile.HasValue)
            {
                // controller は OnEnable で自動初期化されるため、通常は同フレーム内に解決する。ログは出さない。
                _state = TimelineSessionState.Pending;
                return;
            }

            if (timeline == null)
            {
                Fail(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.TimelineNotBound,
                    playingDirector != null ? playingDirector.name : string.Empty, TimelineNotBoundDetail);
                return;
            }

            FacialProfile profile = controller.CurrentProfile.Value;
            FacialCharacterProfileSO profileSource = controller.CharacterSO;

            // (2) Bake 参照
            BakeLocateResult located = FacialTimelineBakeLocator.Locate(timeline, bakeAsset);
            _lastBakeLocate = located;
            TimelineDerivation derivation = AcquireDerivation(timeline, located.Bake, profile);
            var context = new TimelineStaticEvaluationContext(
                playingDirector,
                DirectorResolveStatus.SameObject,
                timeline,
                controller,
                profileSource,
                profile,
                hasProfile: true,
                gazeChannels: profileSource != null ? profileSource.GazeChannels : null,
                bake: located,
                derivation: derivation);
            TimelineDiagnosticsEvaluator.EvaluateBakeAndProfileAreas(_diagnostics, context);
            TimelineDiagnosticsEvaluator.EvaluateLayerMatchArea(_diagnostics, context);

            if (located.Status == BakeLocateStatus.Conflict || located.Status == BakeLocateStatus.LegacyExport)
            {
                FailWithFirstError(TimelineDiagnosticArea.Bake);
                return;
            }

            // (3) レイヤー接続（旧 :state 宣言の検出を含む）
            _connector ??= new TimelineLayerConnector(controller, _binding.Registry, _binding.Slug);
            ConnectOutcome outcome = _connector.Connect(derivation, profile, _binding.BlendShapeNames, located.Bake, _diagnostics);
            if (outcome == ConnectOutcome.LegacyStateDeclaration)
            {
                FailWithFirstError(TimelineDiagnosticArea.LayerConnection);
                return;
            }

            if (outcome == ConnectOutcome.ControllerNotInitialized)
            {
                _state = TimelineSessionState.Pending;
                return;
            }

            _takeover ??= new TimelineChannelTakeover(_binding.Registry, _binding.Slug);
            _takeover.Attach(derivation.Channels, _diagnostics, _binding.BlendShapeNames, CollectDeclaredLayerSourceIds(profile));

            // レイヤー weight トラックがある Timeline だけ live のレイヤー weight を止める（無い Timeline の挙動は変えない）。
            if (TimelineAssetScanner.CollectLayerWeightTracks(timeline).Count > 0)
            {
                _layerWeightOverride.Begin(controller.WeightInjectionGate);
            }

            _sessionBake = located.Bake;
            _sessionProfile = profile;
            BuildExpressionValuePlaybacks(derivation);
            _diagnostics.ReplaceArea(TimelineDiagnosticArea.Session, EmptyItems);
            _conflictDirector = null;
            _hasConflictRecord = false;

            _activeTimeline = timeline;
            _activeDirector = playingDirector;
            _state = TimelineSessionState.Active;

            LogWarningsOnce();
            UpdateLegacyInspection(timeline, profileSource);
        }

        /// <summary>
        /// 指定 Director が Active なセッションの所有者か（別 Director の Mixer が sink に書かないための判定。確保なし）。
        /// </summary>
        public bool IsSessionOwnedBy(PlayableDirector playingDirector)
        {
            return _state == TimelineSessionState.Active && ReferenceEquals(_activeDirector, playingDirector);
        }

        /// <summary>
        /// セッションを解放する（乗っ取りの復元 → レイヤー接続の解放 → Idle）。二重呼び出しは no-op。
        /// </summary>
        public void ReleaseAll()
        {
            // live の weight を先に戻す（停止中の入力源スロット解放は宣言 weight 側にしか反映されないため）。
            _layerWeightOverride.End();
            _takeover?.Release();
            _connector?.Disconnect();
            _sessionBake = null;
            _sessionProfile = default;
            _activeTimeline = null;
            _activeDirector = null;
            _state = TimelineSessionState.Idle;
            _conflictDirector = null;
            _hasConflictRecord = false;
            _warningGate.ResetEpoch();
        }

        // ================================================================
        // 静的診断（Edit / Play 共通）
        // ================================================================

        /// <summary>
        /// controller が保持する Profile（未初期化なら Profile 無し）で静的診断を評価し、診断状態の静的領域を置換する。
        /// Console には出さない。シーン走査を伴うため毎フレーム呼ばない。
        /// </summary>
        public void EvaluateStaticDiagnostics()
        {
            FacialController controller = ResolveController();
            bool hasProfile = controller != null && controller.IsInitialized && controller.CurrentProfile.HasValue;
            EvaluateStaticDiagnostics(hasProfile ? controller.CurrentProfile.Value : default, hasProfile);
        }

        /// <summary>
        /// 指定 Profile で静的診断を評価する（Editor は TimelineProfileSource で解決した Profile を渡す）。
        /// </summary>
        public void EvaluateStaticDiagnostics(FacialProfile profile, bool hasProfile)
        {
            FacialController controller = ResolveController();
            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(this, director, out DirectorResolveStatus status);
            TimelineAsset timeline = resolved != null ? resolved.playableAsset as TimelineAsset : null;
            BakeLocateResult located = FacialTimelineBakeLocator.Locate(timeline, bakeAsset);
            TimelineDerivation derivation = timeline != null && hasProfile
                ? TimelineChannelDeriver.Derive(TimelineAssetScanner.Scan(timeline).Tracks, profile)
                : null;
            TrackBindingReport? trackBindings = _hasTrackBindingReport && _trackBindingDirector == resolved
                ? _trackBindingReport
                : (TrackBindingReport?)null;

            var context = new TimelineStaticEvaluationContext(
                resolved,
                status,
                timeline,
                controller,
                controller != null ? controller.CharacterSO : null,
                profile,
                hasProfile,
                gazeChannels: null,
                bake: located,
                derivation: derivation,
                trackBindings: trackBindings);
            TimelineDiagnosticsEvaluator.EvaluateStatic(this, _diagnostics, context);
        }

        /// <summary>
        /// Track binding 自動設定の結果を記録する（次の静的診断の TrackBinding 領域に使う）。Edit では Inspector の評価が呼ぶ。
        /// </summary>
        internal void RecordTrackBindingReport(PlayableDirector boundDirector, TrackBindingReport report)
        {
            _trackBindingReport = report;
            _trackBindingDirector = boundDirector;
            _hasTrackBindingReport = boundDirector != null;
        }

        /// <summary>指定 Director について記録済みの Track binding 自動設定の結果を返す。</summary>
        internal bool TryGetTrackBindingReport(PlayableDirector boundDirector, out TrackBindingReport report)
        {
            if (_hasTrackBindingReport && boundDirector != null && _trackBindingDirector == boundDirector)
            {
                report = _trackBindingReport;
                return true;
            }

            report = default;
            return false;
        }

        // ================================================================
        // ライフサイクル（Play のみ。Edit の評価は Inspector が行う）
        // ================================================================

        private void OnEnable()
        {
            if (UnityEngine.Application.isPlaying)
            {
                OnEnableInPlay();
            }
        }

        private void Start()
        {
            if (UnityEngine.Application.isPlaying)
            {
                StartInPlay();
            }
        }

        private void OnDisable()
        {
            OnDisableInPlay();
        }

        private void OnDestroy()
        {
            ReleaseAll();
        }

        /// <summary>
        /// Play の OnEnable 本体: Director を解決し、binding 未設定の Facial トラックに自分を設定する。
        /// 設定が発生し graph が既に有効なら RebuildGraph する（Mixer は binding 済みトラックからしか Receiver を呼ばないため）。
        /// </summary>
        internal void OnEnableInPlay()
        {
            _hasTrackBindingReport = false;
            _trackBindingDirector = null;

            PlayableDirector resolved = TimelineTrackBindingResolver.ResolveDirector(this, director, out _);
            if (resolved == null || !(resolved.playableAsset is TimelineAsset timeline))
            {
                return;
            }

            TrackBindingReport report = TimelineTrackBindingResolver.EnsureBindings(
                resolved, timeline, this, RuntimeTrackBindingWriter.Instance);
            _trackBindingReport = report;
            _trackBindingDirector = resolved;
            _hasTrackBindingReport = true;

            if (report.Assigned > 0 && resolved.playableGraph.IsValid())
            {
                resolved.RebuildGraph();
            }
        }

        /// <summary>
        /// Play の Start 本体: 静的診断を評価し、Error / Warning を警告ゲートで 1 回ずつ Console に出す（Info は出さない）。
        /// </summary>
        internal void StartInPlay()
        {
            EvaluateStaticDiagnostics();

            IReadOnlyList<TimelineDiagnosticItem> items = _diagnostics.Items;
            for (int i = 0; i < items.Count; i++)
            {
                TimelineDiagnosticItem item = items[i];
                if (item.Severity == TimelineDiagnosticSeverity.Error)
                {
                    if (_warningGate.TryPass(GetInstanceID(), item.Code, item.Subject))
                    {
                        Debug.LogError(Format(item), this);
                    }
                }
                else if (item.Severity == TimelineDiagnosticSeverity.Warning)
                {
                    if (_warningGate.TryPass(GetInstanceID(), item.Code, item.Subject))
                    {
                        Debug.LogWarning(Format(item), this);
                    }
                }
            }
        }

        /// <summary>OnDisable 本体: セッションを解放する（接続 / 乗っ取りの復元と警告エポックのリセット）。</summary>
        internal void OnDisableInPlay()
        {
            ReleaseAll();
        }

        private FacialController ResolveController()
        {
            if (_hasBinding && _binding.Controller != null)
            {
                return _binding.Controller;
            }

            return GetComponentInParent<FacialController>(true);
        }

        // ================================================================
        // Mixer 向け（署名維持）
        // ================================================================

        public bool TryGetExpressionSink(string layerName, out TimelineExpressionStateSink sink)
        {
            if (_connector == null)
            {
                sink = null;
                return false;
            }

            return _connector.TryGetStateSink(layerName, out sink);
        }

        public bool TryGetExpressionValueSink(string layerName, out TimelineBakedValueSink sink)
        {
            if (_connector == null)
            {
                sink = null;
                return false;
            }

            return _connector.TryGetValueSink(layerName, out sink);
        }

        /// <summary>
        /// Expression トラックの状態イベントから、指定レイヤーの表情の値を作る再生器を作る（HID-318）。
        /// </summary>
        /// <remarks>
        /// 値は Bake のカーブではなく、セッションの Profile とランタイムの trigger 入力源と同じ遷移計算で作る（REC 再生と同じ位相に
        /// するため。<see cref="TimelineExpressionTransitionPlayer"/>）。Bake は値 sink の BlendShape 名（寄与範囲）にだけ使う。
        /// Mixer が sink の切り替わり時にだけ呼ぶ（確保はここに閉じる）。
        /// </remarks>
        public bool TryCreateExpressionTransitionPlayer(
            string layerName,
            TimelineStateEvent[] events,
            out TimelineExpressionTransitionPlayer player)
        {
            player = null;
            if (_state != TimelineSessionState.Active
                || string.IsNullOrEmpty(layerName)
                || !TryGetExpressionValueSink(layerName, out TimelineBakedValueSink sink))
            {
                return false;
            }

            player = new TimelineExpressionTransitionPlayer(
                events,
                sink.BakedBlendShapeNames,
                _sessionProfile,
                _connector.MaxStackDepth,
                ResolveExclusionMode(_sessionProfile, layerName));
            return true;
        }

        /// <summary>
        /// 再生器の現在値を指定レイヤーの値 sink に書く。表情が無く遷移も終わっていれば値 sink を無効化し、レイヤーに寄与させない
        /// （REC 再生の trigger 入力源と同じ）。ヒープ確保しない。
        /// </summary>
        public void ApplyExpressionValues(string layerName, TimelineExpressionTransitionPlayer player)
        {
            if (string.IsNullOrEmpty(layerName) || !TryGetExpressionValueSink(layerName, out TimelineBakedValueSink sink))
            {
                return;
            }

            if (player == null
                || !_playbackByLayer.TryGetValue(layerName, out ExpressionValuePlayback playback)
                || !ReferenceEquals(playback.Sink, sink)
                || playback.Scratch.Length == 0
                || player.BlendShapeCount != playback.Scratch.Length
                || !player.TryWriteValues(playback.Scratch))
            {
                sink.Invalidate();
                return;
            }

            sink.SetValues(playback.Scratch);
        }

        private static ExclusionMode ResolveExclusionMode(FacialProfile profile, string layerName)
        {
            LayerDefinition? layer = profile.FindLayerByName(layerName);
            return layer.HasValue ? layer.Value.ExclusionMode : ExclusionMode.LastWins;
        }

        /// <summary>
        /// レイヤー weight トラックの値を注入する（Active なセッションでのみ有効。live の weight を止められなかった場合は何もしない）。
        /// ヒープ確保しない（未知レイヤーの初回警告を除く）。
        /// </summary>
        public void ApplyLayerWeight(string layerName, float weight)
        {
            if (_state != TimelineSessionState.Active)
            {
                return;
            }

            _layerWeightOverride.Apply(layerName, weight);
        }

        /// <summary>live のレイヤー weight を止めて Timeline のレイヤー weight を注入中か。</summary>
        public bool IsLayerWeightOverrideActive => _layerWeightOverride.IsActive;

        /// <param name="channelSubId">Value トラックの ChannelSubId（乗っ取り先の registry id）。</param>
        public bool TryGetAnalogSink(string channelSubId, out TimelineAnalogInputSource sink)
        {
            if (_takeover == null)
            {
                sink = null;
                return false;
            }

            return _takeover.TryGetAnalogSink(channelSubId, out sink);
        }

        /// <param name="channelSubId">Value トラックの ChannelSubId（乗っ取り先の registry id）。</param>
        public bool TryGetGazeSink(string channelSubId, out TimelineGazeInputSource sink)
        {
            if (_takeover == null)
            {
                sink = null;
                return false;
            }

            return _takeover.TryGetGazeSink(channelSubId, out sink);
        }

        /// <param name="channelSubId">値提供型 Value トラックの ChannelSubId（乗っ取り先の registry id）。</param>
        public bool TryGetValueProviderSink(string channelSubId, out TimelineValueProviderInputSource sink)
        {
            if (_takeover == null)
            {
                sink = null;
                return false;
            }

            return _takeover.TryGetValueProviderSink(channelSubId, out sink);
        }

        // ================================================================
        // セッション資源
        // ================================================================

        private TimelineDerivation AcquireDerivation(TimelineAsset timeline, FacialTimelineBakeAsset bake, FacialProfile profile)
        {
            ExpressionSourceBake[] bakeContent = bake != null ? bake.ExpressionBakes : null;
            string bakeSourceHash = bake != null ? bake.SourceHashHex : null;
            if (_pooledDerivation != null
                && _pooledTimeline == timeline
                && _pooledBake == bake
                && ReferenceEquals(_pooledBakeExpressionBakes, bakeContent)
                && string.Equals(_pooledBakeSourceHash, bakeSourceHash, StringComparison.Ordinal)
                && _hasPooledProfile
                && SameProfileSnapshot(_pooledProfile, profile))
            {
                return _pooledDerivation;
            }

            _pooledDerivation = TimelineChannelDeriver.Derive(TimelineAssetScanner.Scan(timeline).Tracks, profile);
            _pooledTimeline = timeline;
            _pooledBake = bake;
            _pooledBakeExpressionBakes = bakeContent;
            _pooledBakeSourceHash = bakeSourceHash;
            _pooledProfile = profile;
            _hasPooledProfile = true;
            _playbackByLayer.Clear();
            return _pooledDerivation;
        }

        private void ClearPool()
        {
            _pooledDerivation = null;
            _pooledTimeline = null;
            _pooledBake = null;
            _pooledBakeExpressionBakes = null;
            _pooledBakeSourceHash = null;
            _pooledProfile = default;
            _hasPooledProfile = false;
            _playbackByLayer.Clear();
        }

        private static bool SameProfileSnapshot(FacialProfile a, FacialProfile b)
        {
            // Profile は値型だが中身は配列参照なので、同じ配列を指しているか（= 同じスナップショットか）で比べる。
            return a.Layers.Equals(b.Layers)
                && a.Expressions.Equals(b.Expressions)
                && a.LayerInputSources.Equals(b.LayerInputSources);
        }

        /// <summary>Profile の Layer.inputSources に宣言された入力源 id の集合（セッション開始時だけ呼ぶ）。</summary>
        private static HashSet<string> CollectDeclaredLayerSourceIds(FacialProfile profile)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            ReadOnlySpan<InputSourceDeclaration[]> declarations = profile.LayerInputSources.Span;
            for (int layer = 0; layer < declarations.Length; layer++)
            {
                InputSourceDeclaration[] layerDeclarations = declarations[layer];
                for (int i = 0; layerDeclarations != null && i < layerDeclarations.Length; i++)
                {
                    if (!string.IsNullOrEmpty(layerDeclarations[i].Id))
                    {
                        ids.Add(layerDeclarations[i].Id);
                    }
                }
            }

            return ids;
        }

        /// <summary>接続済みの全レイヤーについて値 sink に書く作業バッファを先行構築する（ProcessFrame 中の確保を無くす）。</summary>
        private void BuildExpressionValuePlaybacks(TimelineDerivation derivation)
        {
            IReadOnlyList<TimelineLayerDescriptor> layers = derivation.Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                string layerName = layers[i].LayerName;
                if (!_connector.TryGetValueSink(layerName, out TimelineBakedValueSink sink))
                {
                    continue;
                }

                if (_playbackByLayer.TryGetValue(layerName, out ExpressionValuePlayback existing)
                    && ReferenceEquals(existing.Sink, sink))
                {
                    continue;
                }

                float[] scratch = sink.BakedValueCount == 0 ? Array.Empty<float>() : new float[sink.BakedValueCount];
                _playbackByLayer[layerName] = new ExpressionValuePlayback(sink, scratch);
            }
        }

        // ================================================================
        // 診断と Console 出力
        // ================================================================

        private void Fail(TimelineDiagnosticArea area, TimelineDiagnosticCode code, string subject, string detail)
        {
            var item = new TimelineDiagnosticItem(area, code, TimelineDiagnosticSeverity.Error, subject ?? string.Empty, detail);
            _diagnostics.ReplaceArea(area, new[] { item });
            EnterFailed(item);
        }

        private void FailWithFirstError(TimelineDiagnosticArea area)
        {
            IReadOnlyList<TimelineDiagnosticItem> items = _diagnostics.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Area == area && items[i].Severity == TimelineDiagnosticSeverity.Error)
                {
                    EnterFailed(items[i]);
                    return;
                }
            }

            _state = TimelineSessionState.Failed;
        }

        private void EnterFailed(in TimelineDiagnosticItem firstError)
        {
            _layerWeightOverride.End();
            _connector?.Disconnect();
            _takeover?.Release();
            _activeTimeline = null;
            _activeDirector = null;
            _sessionBake = null;
            _sessionProfile = default;
            _state = TimelineSessionState.Failed;
            if (_warningGate.TryPass(GetInstanceID(), firstError.Code, firstError.Subject))
            {
                Debug.LogError(Format(firstError), this);
            }
        }

        private void RecordSessionConflict(PlayableDirector otherDirector)
        {
            // 所有者でない Director の Mixer は毎フレーム呼ぶため、記録済みの競合相手なら文字列を作らずに戻る（確保なし）。
            if (_hasConflictRecord && ReferenceEquals(_conflictDirector, otherDirector))
            {
                return;
            }

            _hasConflictRecord = true;
            _conflictDirector = otherDirector;
            string subject = otherDirector != null ? otherDirector.name : string.Empty;
            if (_diagnostics.Contains(TimelineDiagnosticCode.SessionConflict, subject))
            {
                return;
            }

            var item = new TimelineDiagnosticItem(
                TimelineDiagnosticArea.Session,
                TimelineDiagnosticCode.SessionConflict,
                TimelineDiagnosticSeverity.Error,
                subject,
                SessionConflictDetail);
            _diagnostics.ReplaceArea(TimelineDiagnosticArea.Session, new[] { item });
            if (_warningGate.TryPass(GetInstanceID(), item.Code, item.Subject))
            {
                Debug.LogError(Format(item), this);
            }
        }

        private void LogWarningsOnce()
        {
            IReadOnlyList<TimelineDiagnosticItem> items = _diagnostics.Items;
            for (int i = 0; i < items.Count; i++)
            {
                TimelineDiagnosticItem item = items[i];
                if (item.Severity == TimelineDiagnosticSeverity.Warning
                    && _warningGate.TryPass(GetInstanceID(), item.Code, item.Subject))
                {
                    Debug.LogWarning(Format(item), this);
                }
            }
        }

        private static string Format(in TimelineDiagnosticItem item)
        {
            return string.IsNullOrEmpty(item.Subject)
                ? LogPrefix + item.Code + ": " + item.Detail
                : LogPrefix + item.Code + " '" + item.Subject + "': " + item.Detail;
        }

        /// <summary>Editor の再ベイク修復（旧通知）向けに、Bake の鮮度判定を旧ステータスへ写して通知する。</summary>
        private void UpdateLegacyInspection(TimelineAsset timeline, FacialCharacterProfileSO profileSource)
        {
            BakeInspectionStatus status;
            if (_sessionBake == null)
            {
                status = BakeInspectionStatus.MissingBakeAsset;
            }
            else if (_diagnostics.Contains(TimelineDiagnosticCode.BakeStale)
                     || _diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch))
            {
                status = BakeInspectionStatus.HashMismatch;
            }
            else
            {
                status = BakeInspectionStatus.Fresh;
            }

            LastBakeInspectionStatus = status;
            if (status == BakeInspectionStatus.Fresh || !UnityEngine.Application.isEditor)
            {
                return;
            }

            BakeIssueDetected?.Invoke(new BakeInspectionIssue(this, timeline, profileSource, status));
        }

        private readonly struct ExpressionValuePlayback
        {
            public ExpressionValuePlayback(TimelineBakedValueSink sink, float[] scratch)
            {
                Sink = sink;
                Scratch = scratch ?? Array.Empty<float>();
            }

            public TimelineBakedValueSink Sink { get; }

            public float[] Scratch { get; }
        }
    }

    public enum BakeInspectionStatus
    {
        NotChecked = 0,
        MissingBakeAsset = 1,
        HashMismatch = 2,
        Fresh = 3,
    }

    public readonly struct BakeInspectionIssue
    {
        public BakeInspectionIssue(
            FacialTimelineReceiver receiver,
            TimelineAsset timeline,
            FacialCharacterProfileSO profileSource,
            BakeInspectionStatus status)
        {
            Receiver = receiver;
            Timeline = timeline;
            ProfileSource = profileSource;
            Status = status;
        }

        public FacialTimelineReceiver Receiver { get; }

        public TimelineAsset Timeline { get; }

        public FacialCharacterProfileSO ProfileSource { get; }

        public BakeInspectionStatus Status { get; }
    }
}
