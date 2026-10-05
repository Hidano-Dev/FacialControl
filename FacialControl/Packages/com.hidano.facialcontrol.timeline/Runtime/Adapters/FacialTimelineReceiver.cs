using System;
using System.Collections.Generic;
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
    /// Timeline 再生の設定・状態・診断の集約点（ファサード）。
    /// </summary>
    /// <remarks>
    /// <para>binding（<c>TimelineAdapterBinding.OnStart</c>）から接続コンテキストを受け取り、Mixer が呼ぶ
    /// <see cref="BeginPlaybackSession"/> で Bake 解決 → レイヤー導出 → 接続（<see cref="TimelineLayerConnector"/>）→
    /// Analog / Gaze の乗っ取り（<see cref="TimelineChannelTakeover"/>）を行う。</para>
    /// <para>Failed の判定順: (1) binding 未接続 / 無効 / Receiver が controller と別 GameObject → (2) Bake 参照の不整合 / 旧形式 →
    /// (3) 旧 <c>:state</c> 宣言。最初に見つかった Error だけを Console に 1 回出す（Inspector は診断状態の全件を読む）。
    /// Profile 内容ハッシュ不一致（ProfileMismatch）と Source ハッシュ不一致（BakeStale）は Warning で、Active のまま Bake の値を再生する。</para>
    /// <para>Profile は controller が保持する値（<see cref="FacialController.CurrentProfile"/>）を使い、再読込しない。</para>
    /// <para>(Timeline, Bake, Profile) が同じ間はセッション資源（導出結果・sink・Bake → sink のバインディング）を再利用する。</para>
    /// <para>メインスレッド専用。Active 中の Mixer 向け API（TryGet* / <see cref="SampleExpressionValues"/>）はヒープ確保しない。</para>
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
        private readonly Dictionary<string, ExpressionBakePlayback> _playbackByLayer =
            new Dictionary<string, ExpressionBakePlayback>(StringComparer.Ordinal);

        private TimelineBindingContext _binding;
        private bool _hasBinding;
        private TimelineLayerConnector _connector;
        private TimelineChannelTakeover _takeover;

        private TimelineSessionState _state = TimelineSessionState.Idle;
        private TimelineAsset _activeTimeline;
        private PlayableDirector _activeDirector;
        private BakeLocateResult _lastBakeLocate;
        private FacialTimelineBakeAsset _sessionBake;

        // セッション資源プールのキー（(Timeline, Bake, Profile) が同じ間は導出結果と Bake → sink のバインディングを再利用する）。
        private TimelineAsset _pooledTimeline;
        private FacialTimelineBakeAsset _pooledBake;
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

        /// <summary>Analog / Gaze の乗っ取りエントリ（Inspector 表示用）。</summary>
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
                    if (timeline != _activeTimeline || playingDirector != _activeDirector)
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
                if (GetComponent<FacialController>() == null)
                {
                    Fail(TimelineDiagnosticArea.Placement, TimelineDiagnosticCode.ReceiverNotOnControllerObject, name, ReceiverNotOnControllerDetail);
                }
                else
                {
                    Fail(TimelineDiagnosticArea.ProfileBinding, TimelineDiagnosticCode.BindingMissing, name, BindingMissingDetail);
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
            _takeover.Attach(derivation.Channels, _diagnostics);

            _sessionBake = located.Bake;
            BuildExpressionBakePlaybacks(derivation, located.Bake);
            _diagnostics.ReplaceArea(TimelineDiagnosticArea.Session, EmptyItems);

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
            return _state == TimelineSessionState.Active && _activeDirector == playingDirector;
        }

        /// <summary>
        /// セッションを解放する（乗っ取りの復元 → レイヤー接続の解放 → Idle）。二重呼び出しは no-op。
        /// </summary>
        public void ReleaseAll()
        {
            _takeover?.Release();
            _connector?.Disconnect();
            _sessionBake = null;
            _activeTimeline = null;
            _activeDirector = null;
            _state = TimelineSessionState.Idle;
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
        /// セッション開始時に構築した Bake → 値 sink のバインディングで、指定時刻のカーブ値を値 sink に書く。
        /// 値カーブが無いレイヤーは値 sink を無効化する。ヒープ確保しない。
        /// </summary>
        public void SampleExpressionValues(string layerName, double timeSeconds)
        {
            if (string.IsNullOrEmpty(layerName) || !TryGetExpressionValueSink(layerName, out TimelineBakedValueSink sink))
            {
                return;
            }

            if (_sessionBake == null
                || !_playbackByLayer.TryGetValue(layerName, out ExpressionBakePlayback playback)
                || !ReferenceEquals(playback.Sink, sink)
                || playback.Bindings.Length == 0)
            {
                sink.Invalidate();
                return;
            }

            CurveBinding[] bindings = playback.Bindings;
            for (int i = 0; i < bindings.Length; i++)
            {
                CurveBinding binding = bindings[i];
                float value = binding.Curve != null ? binding.Curve.Evaluate((float)timeSeconds) : 0f;
                sink.SetValue(binding.BufferIndex, value);
            }
        }

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

        private void OnDisable()
        {
            ReleaseAll();
        }

        private void OnDestroy()
        {
            ReleaseAll();
        }

        // ================================================================
        // セッション資源
        // ================================================================

        private TimelineDerivation AcquireDerivation(TimelineAsset timeline, FacialTimelineBakeAsset bake, FacialProfile profile)
        {
            if (_pooledDerivation != null
                && _pooledTimeline == timeline
                && _pooledBake == bake
                && _hasPooledProfile
                && SameProfileSnapshot(_pooledProfile, profile))
            {
                return _pooledDerivation;
            }

            _pooledDerivation = TimelineChannelDeriver.Derive(TimelineAssetScanner.Scan(timeline).Tracks, profile);
            _pooledTimeline = timeline;
            _pooledBake = bake;
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

        /// <summary>接続済みの全レイヤーについて Bake → 値 sink のバインディングを先行構築する（ProcessFrame 中の辞書追加を無くす）。</summary>
        private void BuildExpressionBakePlaybacks(TimelineDerivation derivation, FacialTimelineBakeAsset bake)
        {
            IReadOnlyList<TimelineLayerDescriptor> layers = derivation.Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                string layerName = layers[i].LayerName;
                if (!_connector.TryGetValueSink(layerName, out TimelineBakedValueSink sink))
                {
                    continue;
                }

                if (_playbackByLayer.TryGetValue(layerName, out ExpressionBakePlayback existing)
                    && ReferenceEquals(existing.Sink, sink))
                {
                    continue;
                }

                _playbackByLayer[layerName] = BuildExpressionBakePlayback(bake, layerName, sink);
            }
        }

        private static ExpressionBakePlayback BuildExpressionBakePlayback(
            FacialTimelineBakeAsset bake,
            string layerName,
            TimelineBakedValueSink sink)
        {
            if (bake == null || bake.ExpressionBakes == null)
            {
                return new ExpressionBakePlayback(sink, Array.Empty<CurveBinding>());
            }

            for (int bakeIndex = 0; bakeIndex < bake.ExpressionBakes.Length; bakeIndex++)
            {
                ExpressionSourceBake expressionBake = bake.ExpressionBakes[bakeIndex];
                if (expressionBake == null || !string.Equals(expressionBake.LayerName, layerName, StringComparison.Ordinal))
                {
                    continue;
                }

                BlendShapeCurve[] curves = expressionBake.Curves ?? Array.Empty<BlendShapeCurve>();
                var bindings = new List<CurveBinding>(curves.Length);
                for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                {
                    BlendShapeCurve curve = curves[curveIndex];
                    if (curve == null
                        || string.IsNullOrEmpty(curve.BlendShapeName)
                        || !sink.TryGetBufferIndex(curve.BlendShapeName, out int bufferIndex))
                    {
                        continue;
                    }

                    bindings.Add(new CurveBinding(bufferIndex, curve.Curve));
                }

                return new ExpressionBakePlayback(sink, bindings.ToArray());
            }

            return new ExpressionBakePlayback(sink, Array.Empty<CurveBinding>());
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
            _connector?.Disconnect();
            _takeover?.Release();
            _activeTimeline = null;
            _activeDirector = null;
            _sessionBake = null;
            _state = TimelineSessionState.Failed;
            if (_warningGate.TryPass(GetInstanceID(), firstError.Code, firstError.Subject))
            {
                Debug.LogError(Format(firstError), this);
            }
        }

        private void RecordSessionConflict(PlayableDirector otherDirector)
        {
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

        private readonly struct ExpressionBakePlayback
        {
            public ExpressionBakePlayback(TimelineBakedValueSink sink, CurveBinding[] bindings)
            {
                Sink = sink;
                Bindings = bindings ?? Array.Empty<CurveBinding>();
            }

            public TimelineBakedValueSink Sink { get; }

            public CurveBinding[] Bindings { get; }
        }

        private readonly struct CurveBinding
        {
            public CurveBinding(int bufferIndex, AnimationCurve curve)
            {
                BufferIndex = bufferIndex;
                Curve = curve;
            }

            public int BufferIndex { get; }

            public AnimationCurve Curve { get; }
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
