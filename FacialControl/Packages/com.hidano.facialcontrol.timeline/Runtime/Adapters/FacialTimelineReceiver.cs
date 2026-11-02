using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
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
    /// Timeline 再生の設定・状態・診断の集約点（ファサード）。キャラ 1 体（FacialController）につき 1 つで、ユーザーは置かない。
    /// </summary>
    /// <remarks>
    /// <para>Facial Character トラックに Bind された FacialController の GameObject に、子トラックの Mixer が
    /// <see cref="GetOrCreate"/> で内部実装として置く（シーンには保存しない。HID-408）。Inspector には診断が出る。</para>
    /// <para>接続コンテキスト（registry・Profile・BlendShape 名）は controller の公開 API から取り、controller の
    /// <see cref="FacialController.Deinitializing"/> で解放する（Profile の Adapter Bindings への追加は要らない）。Mixer が呼ぶ
    /// <see cref="BeginPlaybackSession"/> で Bake 解決 → レイヤー導出 → 接続（<see cref="TimelineLayerConnector"/>）→
    /// Analog / Gaze の乗っ取り（<see cref="TimelineChannelTakeover"/>）を行う。</para>
    /// <para>Failed の判定順: (1) controller が無い / 親トラックの外にある旧構造の Facial トラック / 親トラックが複数 →
    /// (2) Bake 参照の不整合 / 旧形式 → (3) 旧 <c>:state</c> 宣言。最初に見つかった Error だけを Console に 1 回出す
    /// （Inspector は診断状態の全件を読む）。
    /// Profile 内容ハッシュ不一致（ProfileMismatch）と Source ハッシュ不一致（BakeStale）は Warning で、Active のまま再生する。</para>
    /// <para>Play 中の表情の値は Bake のカーブではなく、セッションの Profile とランタイムの trigger 入力源と同じ遷移計算で作る
    /// （<see cref="TryCreateExpressionTransitionPlayer"/>。REC 再生と同じ位相にするため。HID-318）。Bake は Edit プレビューと、
    /// 値 sink の BlendShape 名（レイヤーへの寄与範囲）に使う。</para>
    /// <para>Profile は controller が保持する値（<see cref="FacialController.CurrentProfile"/>）を使い、再読込しない。</para>
    /// <para>(Timeline, Bake, Profile) が同じ間はセッション資源（導出結果・sink・Bake → sink のバインディング）を再利用する。
    /// 同じ Bake インスタンスでも再ベイクで中身（ExpressionBakes / SourceHashHex）が変わったら作り直す。</para>
    /// <para>メインスレッド専用。Active 中の Mixer 向け API（TryGet* / <see cref="ApplyExpressionValues"/>）はヒープ確保しない。</para>
    /// </remarks>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class FacialTimelineReceiver : MonoBehaviour
    {
        /// <summary>
        /// Editor の再ベイク修復向けの旧通知（Profile / Source ハッシュ不一致・Bake 無し）。Editor 購読の一元化（9.x）で置き換える。
        /// </summary>
        public static event Action<BakeInspectionIssue> BakeIssueDetected;

        /// <summary>接続コンテキストの slug（値 sink の id の prefix。<c>timeline:{レイヤー名}</c>）。</summary>
        public static readonly AdapterSlug TimelineSlug = AdapterSlug.Parse("timeline");

        private const string LogPrefix = "[FacialTimelineReceiver] ";
        private const string ControllerMissingDetail =
            "FacialController が見つかりません。Facial Character トラックには FacialController を Bind してください。";
        private const string TimelineNotBoundDetail =
            "PlayableDirector に TimelineAsset がセットされていません。Director の Playable 欄に Export した TimelineAsset をセットしてください。";
        private const string SessionConflictDetail =
            "この Receiver は別の PlayableDirector / TimelineAsset で再生中です。1 つの Receiver で同時に再生できるのは 1 つの Director だけです。";

        private static readonly string[] EmptyNames = Array.Empty<string>();
        private static readonly TimelineTakeoverEntry[] EmptyTakeoverEntries = Array.Empty<TimelineTakeoverEntry>();
        private static readonly TimelineDiagnosticItem[] EmptyItems = Array.Empty<TimelineDiagnosticItem>();

        [SerializeField] private FacialTimelineBakeAsset bakeAsset;

        // Director の上書き（テスト・Inspector 用。保存しない）。
        [NonSerialized] private PlayableDirector _directorOverride;

        // 接続コンテキストを controller から自動で取ったか（テストが直接渡した接続は controller の破棄通知で外さない）。
        [NonSerialized] private bool _bindingFromController;
        [NonSerialized] private FacialController _subscribedController;

        // GetOrCreate が作った Receiver の対象（Edit の一時 Receiver は controller と別の GameObject にある）。
        [NonSerialized] private FacialController _boundController;

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

        /// <summary>
        /// 静的診断に使う Director の上書き（保存しない）。未指定なら、この controller を Bind した Facial Character トラックを持つ
        /// Director をシーンから探す。
        /// </summary>
        public PlayableDirector DirectorOverride
        {
            get => _directorOverride;
            set => _directorOverride = value;
        }

        /// <summary>
        /// controller の Receiver を返す。無ければ内部実装として作る。controller が null、またはシーンに無い（Prefab アセット等）なら null。
        /// </summary>
        /// <remarks>
        /// <para>controller の GameObject に既に Receiver があればそれを使う（手で置いた旧来の Receiver を含む）。</para>
        /// <para>Play では controller の GameObject に追加する（実行時の変更はシーンに保存されない）。</para>
        /// <para>Edit ではユーザーのシーン・Prefab インスタンスを書き換えないよう、controller の GameObject には追加せず、
        /// 非表示・非保存（<see cref="EditReceiverHideFlags"/>）の一時 GameObject に持つ（controller ごとに 1 つ。
        /// Play 突入時・アセンブリ再読込時に破棄する）。プレビュー・Inspector・AnimationTrack 変換の副作用で Undo の無い変更や
        /// Prefab の override を作らないため（HID-408 レビュー指摘 6）。</para>
        /// <para>既にあれば確保しない（Mixer がキャッシュ切れのたびに呼ぶ）。</para>
        /// </remarks>
        public static FacialTimelineReceiver GetOrCreate(FacialController controller)
        {
            if (controller == null)
            {
                return null;
            }

            if (controller.TryGetComponent(out FacialTimelineReceiver existing))
            {
                return existing;
            }

            GameObject host = controller.gameObject;
            if (!host.scene.IsValid())
            {
                return null;
            }

            // Mixer と同じ Edit / Play の判定を使う（テストの Play 相当でも controller に追加する）。
            if (Playables.FacialTimelinePlayMode.IsPlaying)
            {
                var created = host.AddComponent<FacialTimelineReceiver>();
                created._boundController = controller;
                return created;
            }

            return GetOrCreateEditReceiver(controller);
        }

        /// <summary>
        /// この Receiver が再生する FacialController。<see cref="GetOrCreate"/> が作った Receiver は作成時の controller、
        /// controller の GameObject に置かれた Receiver はその GameObject の controller。
        /// </summary>
        public FacialController Controller => _boundController != null ? _boundController : GetComponent<FacialController>();

        /// <summary>
        /// 生きている Receiver を集める（シーンにあるもの + Edit の一時 Receiver）。Edit の一時 Receiver は非保存のため
        /// <c>FindObjectsByType</c> では見つからない。シーン走査を伴うため毎フレーム呼ばない。
        /// </summary>
        public static List<FacialTimelineReceiver> CollectAll()
        {
            PruneEditReceivers();
            var results = new List<FacialTimelineReceiver>(
                UnityEngine.Object.FindObjectsByType<FacialTimelineReceiver>(FindObjectsInactive.Include, FindObjectsSortMode.None));
            foreach (FacialTimelineReceiver receiver in EditReceivers.Values)
            {
                if (receiver != null && !results.Contains(receiver))
                {
                    results.Add(receiver);
                }
            }

            return results;
        }

        /// <summary>Edit の一時 Receiver（controller の instanceID → Receiver）。</summary>
        private static readonly Dictionary<int, FacialTimelineReceiver> EditReceivers = new Dictionary<int, FacialTimelineReceiver>();

        private static readonly List<int> StaleEditReceiverKeys = new List<int>();

        /// <summary>テスト用: 生きている Edit の一時 Receiver の数。</summary>
        internal static int EditReceiverCount
        {
            get
            {
                PruneEditReceivers();
                return EditReceivers.Count;
            }
        }

        private static FacialTimelineReceiver GetOrCreateEditReceiver(FacialController controller)
        {
            int key = controller.GetInstanceID();
            if (EditReceivers.TryGetValue(key, out FacialTimelineReceiver cached) && cached != null && cached._boundController == controller)
            {
                return cached;
            }

            PruneEditReceivers();
            // HideAndDontSave は NotEditable を含み、埋め込み Inspector のボタンが押せなくなるので、非保存 + Hierarchy 非表示だけにする。
            var holder = new GameObject("FacialTimelineReceiver (" + controller.name + ")")
            {
                hideFlags = EditReceiverHideFlags,
            };
            var receiver = holder.AddComponent<FacialTimelineReceiver>();
            receiver.hideFlags = EditReceiverHideFlags;
            receiver._boundController = controller;
            EditReceivers[key] = receiver;
            return receiver;
        }

        private static void PruneEditReceivers()
        {
            StaleEditReceiverKeys.Clear();
            foreach (KeyValuePair<int, FacialTimelineReceiver> pair in EditReceivers)
            {
                if (pair.Value == null || pair.Value._boundController == null)
                {
                    StaleEditReceiverKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < StaleEditReceiverKeys.Count; i++)
            {
                if (EditReceivers.TryGetValue(StaleEditReceiverKeys[i], out FacialTimelineReceiver stale) && stale != null)
                {
                    DestroyHolder(stale);
                }

                EditReceivers.Remove(StaleEditReceiverKeys[i]);
            }

            StaleEditReceiverKeys.Clear();
        }

        /// <summary>Edit の一時 Receiver をすべて破棄する（Play 突入・アセンブリ再読込の前。テストの後始末にも使う）。</summary>
        internal static void DestroyEditReceivers()
        {
            foreach (FacialTimelineReceiver receiver in EditReceivers.Values)
            {
                if (receiver != null)
                {
                    DestroyHolder(receiver);
                }
            }

            EditReceivers.Clear();
        }

        /// <summary>Edit の一時 Receiver の HideFlags（非保存・Hierarchy 非表示。編集は許す）。</summary>
        internal const HideFlags EditReceiverHideFlags = HideFlags.DontSave | HideFlags.HideInHierarchy;

        private static void DestroyHolder(FacialTimelineReceiver receiver)
        {
            // Edit では OnDestroy が呼ばれないので、破棄の前に接続と controller の破棄通知の購読を必ず外す。
            receiver.DetachBinding();
            GameObject holder = receiver.gameObject;
            if (UnityEngine.Application.isPlaying)
            {
                UnityEngine.Object.Destroy(holder);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(holder);
            }
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterEditReceiverCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= DestroyEditReceivers;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += DestroyEditReceivers;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
        {
            if (change == UnityEditor.PlayModeStateChange.ExitingEditMode)
            {
                DestroyEditReceivers();
            }
        }
#endif

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
        /// 接続コンテキストを直接渡す（テスト用。controller の child scope を作らない EditMode で registry を差し込む）。
        /// 接続はセッション開始まで遅らせる。既に接続していれば先に解放する。
        /// </summary>
        internal void AttachBinding(in TimelineBindingContext context)
        {
            DetachBinding();
            _binding = context;
            _hasBinding = true;
        }

        /// <summary>
        /// セッションを解放し、接続コンテキストと接続 / 乗っ取りの資源を破棄する（controller の破棄通知でも呼ぶ）。二重呼び出しは no-op。
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
            _bindingFromController = false;
            UnsubscribeController();
            ClearPool();
        }

        /// <summary>
        /// controller の公開 API から接続コンテキストを作って接続する（HID-408。Profile の Adapter Bindings は使わない）。
        /// controller が未初期化・registry が無いときは false（呼び出し側は Pending にする）。
        /// </summary>
        private bool TryAttachFromController(FacialController controller)
        {
            IInputSourceRegistry registry = controller.InputSourceRegistry;
            if (!controller.IsInitialized || !controller.CurrentProfile.HasValue || registry == null)
            {
                return false;
            }

            if (_hasBinding && _bindingFromController && ReferenceEquals(_binding.Registry, registry)
                && ReferenceEquals(_binding.Controller, controller))
            {
                return true;
            }

            AttachBinding(new TimelineBindingContext(
                TimelineSlug,
                controller.CurrentProfile.Value,
                controller.BlendShapeNames,
                registry,
                controller,
                enabled: true));
            _bindingFromController = true;
            _subscribedController = controller;
            controller.Deinitializing += OnControllerDeinitializing;
            return true;
        }

        private void OnControllerDeinitializing(FacialController controller)
        {
            // registry とレイヤー構成が破棄される前に、後付け接続と乗っ取りを解放する（再初期化後は次のセッション開始で取り直す）。
            DetachBinding();
        }

        private void UnsubscribeController()
        {
            if (!ReferenceEquals(_subscribedController, null))
            {
                _subscribedController.Deinitializing -= OnControllerDeinitializing;
                _subscribedController = null;
            }
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

            // (1) controller・トラック構造
            FacialController controller = _hasBinding && _binding.Controller != null
                ? _binding.Controller
                : Controller;
            if (controller == null)
            {
                Fail(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ControllerMissing, name, ControllerMissingDetail);
                return;
            }

            if (!controller.IsInitialized || !controller.CurrentProfile.HasValue)
            {
                // controller は OnEnable で自動初期化されるため、通常は同フレーム内に解決する。ログは出さない。
                _state = TimelineSessionState.Pending;
                return;
            }

            if (!(_hasBinding && !_bindingFromController) && !TryAttachFromController(controller))
            {
                // registry がまだ無い（child scope の構築前）。次のフレームで再試行する。
                _state = TimelineSessionState.Pending;
                return;
            }

            if (timeline == null)
            {
                Fail(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.TimelineNotBound,
                    playingDirector != null ? playingDirector.name : string.Empty, TimelineNotBoundDetail);
                return;
            }

            if (TimelineDiagnosticsEvaluator.EvaluateTrackStructureArea(_diagnostics, timeline))
            {
                FailWithFirstError(TimelineDiagnosticArea.TrackBinding);
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
        /// <param name="director">評価に使う Director（null ならシーン走査で解決）。呼び出し側の文脈で渡し、Receiver には保存しない。</param>
        public void EvaluateStaticDiagnostics(FacialProfile profile, bool hasProfile, PlayableDirector director = null)
        {
            FacialController controller = ResolveController();
            DirectorResolveStatus status = DirectorResolveStatus.Override;
            PlayableDirector resolved = director != null
                ? director
                : TimelineTrackBindingResolver.ResolveDirector(this, _directorOverride, out status);
            TimelineAsset timeline = resolved != null ? resolved.playableAsset as TimelineAsset : null;
            BakeLocateResult located = FacialTimelineBakeLocator.Locate(timeline, bakeAsset);
            TimelineDerivation derivation = timeline != null && hasProfile
                ? TimelineChannelDeriver.Derive(TimelineAssetScanner.Scan(timeline).Tracks, profile)
                : null;

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
                derivation: derivation);
            TimelineDiagnosticsEvaluator.EvaluateStatic(this, _diagnostics, context);
        }

        // ================================================================
        // ライフサイクル（Play のみ。Edit の評価は Inspector が行う）
        // ================================================================

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
            DetachBinding();
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

            return Controller;
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
