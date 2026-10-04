using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Adapters.Recording;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using UnityEngine;

namespace Hidano.FacialControl.Rec.Adapters.Playable
{
    /// <summary>
    /// MonoBehaviour facade that wires REC recording and playback to a FacialController instance.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("FacialControl/REC Character Binding")]
    public sealed class RecCharacterBinding : MonoBehaviour
    {
        [SerializeField]
        private FacialController _facialController;

        [SerializeField]
        [Tooltip("Recording Name を省略したときの既定名。空のままなら take-yyyyMMdd-HHmmss で命名する。")]
        private string _defaultRecordingName = string.Empty;

        [SerializeField]
        [Tooltip("記録タイムスタンプに加算する開始オフセット（秒、0 以上）。録画長にも加算されるため、再生時はこの秒数の先頭待ちが入る。次の録画開始から反映される。")]
        private double _recordingStartOffsetSeconds;

        private FacialController _runtimeController;
        private PlaybackUseCase _playbackUseCase;
        private RecAnalogInjector _analogInjector;
        private RecTriggerInjector _triggerInjector;
        private RecExpressionInjector _expressionInjector;
        private RecValueProviderInjector _valueProviderInjector;
        private RecordingUseCase _recordingUseCase;
        private RecStreamWriter _streamWriter;
        private RecTimeline _loadedTimeline;
        private string _loadedRecordingName;
        private string _loadedRecordingPath;
        private string _lastRecordingName;
        private string _lastRecordingPath;
        private string _lastRequestedRecordingName;
        private string _requestedRecordingName;
        private string _requestedRecordingPath;
        private DateTime _recordingStartedUtc;

        /// <summary>ファイルシステムのタイムスタンプ分解能（FAT 系は 2 秒）を見込んだ許容幅。</summary>
        private const double RecordingStartTimestampToleranceSeconds = 2d;

        public bool IsRecording => _recordingUseCase != null && _recordingUseCase.IsRecording;

        public RecordingState RecordingState => _recordingUseCase?.State ?? RecordingState.Idle;

        public RecPlaybackState PlaybackState => _playbackUseCase?.State ?? RecPlaybackState.Idle;

        /// <summary>
        /// 録画中は記録タイムスタンプの現在値（<see cref="RecordingStartOffsetSeconds"/> を含む）、
        /// 再生中は再生位置の秒数を返す。
        /// </summary>
        public double ElapsedSeconds
        {
            get
            {
                if (_recordingUseCase != null && _recordingUseCase.IsRecording)
                {
                    return _recordingUseCase.ElapsedSeconds;
                }

                return _playbackUseCase?.ElapsedSeconds ?? 0d;
            }
        }

        /// <summary>
        /// 直近の録画で実際に保存したテイク名（同名衝突時の連番付与後）。<see cref="LoadRecording"/> にそのまま渡せる。
        /// 出力ファイルはライタースレッドが開くため、<see cref="StartRecording"/> 直後はまだ前回の値のことがある
        /// （開いた後の Update か <see cref="StopRecording"/> で反映される）。
        /// </summary>
        public string LastRecordingName => _lastRecordingName;

        /// <summary>直近の録画で実際に保存したファイルパス（同名衝突時の連番付与後）。反映タイミングは <see cref="LastRecordingName"/> と同じ。</summary>
        public string LastRecordingPath => _lastRecordingPath;

        /// <summary>録画中のテイクの出力ファイルパス。録画中でない、またはライタースレッドがまだファイルを開いていなければ null。</summary>
        public string CurrentRecordingPath => IsRecording ? _streamWriter?.OutputFilePath : null;

        public string LoadedRecordingPath => _loadedRecordingPath;

        public event Action Completed;

        public FacialController FacialController
        {
            get => _facialController;
            set => _facialController = value;
        }

        /// <summary>
        /// 記録に使うクロック。null（既定）なら <see cref="RecStopwatchClock"/> を使う。
        /// 外部タイムコード等に同期させたいときに差し替える。<see cref="StartRecording"/> 時に読まれ、
        /// 録画中に差し替えても次の録画から反映される。録画開始ごとに <see cref="IRecClock.Reset"/> が呼ばれるため、
        /// 同時に録画する複数の binding で 1 つのインスタンスを共有しないこと。契約は <see cref="IRecClock"/> を参照。
        /// </summary>
        public IRecClock RecordingClock { get; set; }

        /// <summary>
        /// 記録タイムスタンプ（と録画長）に加算する開始オフセット（秒）。有限かつ 0 以上。
        /// <see cref="StartRecording"/> 時に読まれ、録画中に変更しても次の録画から反映される。
        /// </summary>
        public double RecordingStartOffsetSeconds
        {
            get => _recordingStartOffsetSeconds;
            set
            {
                if (!RecordingUseCase.IsValidStartOffset(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Recording start offset must be a finite, non-negative number of seconds.");
                }

                _recordingStartOffsetSeconds = value;
            }
        }

        /// <summary>
        /// Inspector の Default Recording Name。<see cref="Record()"/> / <see cref="Load()"/> などの引数なし操作が読むテイク名。
        /// uGUI の InputField などから UnityEvent で直接設定できる。null は空文字として扱う。
        /// </summary>
        public string RecordingName
        {
            get => _defaultRecordingName;
            set => _defaultRecordingName = value ?? string.Empty;
        }

        /// <summary>
        /// UnityEvent（uGUI Button の OnClick 等）向けの void 版 <see cref="StartRecording"/>。
        /// <see cref="RecordingName"/>、それも空なら take-yyyyMMdd-HHmmss で命名する。
        /// </summary>
        public void Record()
        {
            StartRecording();
        }

        /// <summary>UnityEvent 向けの void 版 <see cref="StartRecording"/>。空の名前は <see cref="Record()"/> と同じ扱い。</summary>
        public void Record(string recordingName)
        {
            StartRecording(recordingName);
        }

        /// <summary>
        /// UnityEvent 向けの void 版 <see cref="LoadRecording"/>。<see cref="RecordingName"/> で直近に録画したテイク
        /// （同名衝突で連番付きになったならその連番付きの名前）を読み込む。<see cref="RecordingName"/> が空なら直近に録画したテイクを読み込む。
        /// </summary>
        public void Load()
        {
            Load(null);
        }

        /// <summary>
        /// UnityEvent 向けの void 版 <see cref="LoadRecording"/>。名前を指定すればそのテイクをそのまま読み込み、
        /// 空なら <see cref="Load()"/> と同じ扱い（<see cref="Record(string)"/> の空の名前と揃える）。
        /// 読み込みに失敗すると前に読み込んだテイクも破棄するため、続く <see cref="Play"/> は古いテイクを再生しない。
        /// </summary>
        public void Load(string recordingName)
        {
            // 録画中のテイクを確定させてから、RecordingName に対応する実際の保存名を解決する。
            StopRecording();
            LoadRecording(string.IsNullOrWhiteSpace(recordingName) ? ResolveNameForLoad() : recordingName);
        }

        /// <summary>UnityEvent 向けの void 版 <see cref="StartPlayback"/>。</summary>
        public void Play()
        {
            StartPlayback();
        }

        public bool StartRecording(string recordingName = null)
        {
            if (!TryEnsureReady(out FacialController controller, out FacialProfile profile))
            {
                return false;
            }

            if (IsRecording)
            {
                UnityEngine.Debug.LogWarning("Recording is already active. StartRecording was ignored.");
                return false;
            }

            StopPlayback();

            string assetName = ResolveAssetName(controller);
            string resolvedRecordingName = RecRecordingNaming.Resolve(recordingName, _defaultRecordingName, DateTime.Now);
            if (!RecSidecarPath.TryBuildRecordingFilePath(assetName, resolvedRecordingName, out string requestedFilePath, out string error))
            {
                UnityEngine.Debug.LogWarning($"REC recording start was ignored because the output path was invalid: {error}");
                return false;
            }

            _requestedRecordingName = resolvedRecordingName;

            // 出力先の予約（同名衝突時の連番付与）とファイルのオープンは RecStreamWriter がライタースレッドで行う。
            // 結果（実際のパス / オープン失敗）は Update と StopRecording で SyncRecordingOutput が拾う。
            RecBaselineState baseline = RecBaselineCapture.Capture(
                controller.InputSourceRegistry,
                controller.ExpressionActivationGate,
                controller.BlendShapeCount);
            _requestedRecordingPath = requestedFilePath;
            _recordingStartedUtc = DateTime.UtcNow;
            GetQueueCapacities(controller.BlendShapeCount, out int floatCapacity, out int byteCapacity);
            _streamWriter = new RecStreamWriter(
                requestedFilePath,
                segmentCapacity: 64,
                initialSegments: 4,
                axisFloatCapacityPerSegment: floatCapacity,
                byteCapacityPerSegment: byteCapacity);
            _recordingUseCase = new RecordingUseCase(
                controller.InputObservationBus,
                RecordingClock ?? new RecStopwatchClock(),
                _streamWriter,
                ResolveStartOffsetSeconds());
            _recordingUseCase.StartRecording(baseline, controller.BlendShapeCount);
            return true;
        }

        public void StopRecording()
        {
            DisposeRecordingSession();
        }

        /// <summary>
        /// 指定したテイクを読み込む。<paramref name="recordingName"/> が空なら直近に録画したテイク
        /// （<see cref="LastRecordingName"/>。連番付与後の名前）を読み込む。
        /// </summary>
        public bool LoadRecording(string recordingName = null)
        {
            if (!TryEnsureReady(out FacialController controller, out FacialProfile profile))
            {
                return false;
            }

            // 録画中なら先に止めて、今のテイクを直近のテイクとして確定させてから名前を解決する。
            StopRecording();

            if (string.IsNullOrWhiteSpace(recordingName))
            {
                recordingName = _lastRecordingName;
                if (string.IsNullOrWhiteSpace(recordingName))
                {
                    UnityEngine.Debug.LogWarning("REC load was ignored because no recording name was given and nothing has been recorded yet.");
                    return false;
                }
            }

            StopPlayback();

            string assetName = ResolveAssetName(controller);
            if (!RecSidecarPath.TryBuildRecordingFilePath(assetName, recordingName, out string filePath, out string error))
            {
                UnityEngine.Debug.LogWarning($"REC load was ignored because the input path was invalid: {error}");
                DiscardLoadedRecording();
                return false;
            }

            if (!RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result))
            {
                DiscardLoadedRecording();
                return false;
            }

            EnsurePlaybackSession(controller);

            _loadedTimeline = result.Timeline;
            _loadedRecordingName = recordingName;
            _loadedRecordingPath = filePath;
            return _playbackUseCase.Load(_loadedTimeline, profile) != null;
        }

        /// <summary>
        /// このキャラクターの保存済み録画を、更新日時の新しい順に列挙する（録画中のテイクは含めない）。
        /// 各要素の <see cref="RecRecordingEntry.Name"/> はそのまま <see cref="LoadRecording"/> に渡せる。
        /// Play モード外でも呼べる。ファイル I/O と GC 確保を伴うため、毎フレームではなく一覧の更新が必要なとき
        /// （画面を開いた・録画を止めた等）だけ呼ぶこと。
        /// </summary>
        public IReadOnlyList<RecRecordingEntry> GetRecordings()
        {
            FacialController controller = _facialController != null
                ? _facialController
                : GetComponent<FacialController>();
            if (controller == null)
            {
                return Array.Empty<RecRecordingEntry>();
            }

            // フォルダ名にできないキャラクター名なら空の一覧を返す（警告は録画・読み込みを実際に試みたときに出る）。
            RecSidecarPath.TryListRecordings(
                ResolveAssetName(controller),
                CurrentRecordingPath,
                out IReadOnlyList<RecRecordingEntry> recordings,
                out _);

            if (!IsRecording)
            {
                return recordings;
            }

            // ライタースレッドはファイルを作ってから OutputFilePath を公開するので、列挙中にオープンが進むと
            // 上の除外をすり抜けることがある。列挙後に読み直したパスで除外し、まだ公開前なら録画開始以降に
            // 書かれたファイルを除外して、録画中のテイクを一覧に出さない。
            string currentRecordingPath = CurrentRecordingPath;
            string currentRecordingFullPath = currentRecordingPath != null ? Path.GetFullPath(currentRecordingPath) : null;
            DateTime recordingStartedUtc = _recordingStartedUtc.AddSeconds(-RecordingStartTimestampToleranceSeconds);
            var settled = new List<RecRecordingEntry>(recordings.Count);
            for (int i = 0; i < recordings.Count; i++)
            {
                bool isInProgress = currentRecordingFullPath != null
                    ? string.Equals(Path.GetFullPath(recordings[i].FilePath), currentRecordingFullPath, StringComparison.Ordinal)
                    : recordings[i].LastWriteTimeUtc >= recordingStartedUtc;
                if (!isInProgress)
                {
                    settled.Add(recordings[i]);
                }
            }

            return settled;
        }

        /// <summary>
        /// <see cref="GetRecordings"/> のテイク名だけを同じ順で返す。uGUI の Dropdown 等の選択肢にそのまま使える。
        /// </summary>
        public IReadOnlyList<string> GetRecordingNames()
        {
            IReadOnlyList<RecRecordingEntry> recordings = GetRecordings();
            var names = new string[recordings.Count];
            for (int i = 0; i < names.Length; i++)
            {
                names[i] = recordings[i].Name;
            }

            return names;
        }

        public bool StartPlayback()
        {
            return StartPlayback(0d);
        }

        /// <summary>
        /// 録画の先頭から <paramref name="startOffsetSeconds"/> 秒の位置から再生する。
        /// 開始位置より前のイベントは瞬時に畳み込んで状態を再構築する（トリガーは最終的な on/off、アナログは各入力源の最後の値）。
        /// 開始位置で遷移途中だった表情は遷移の進行度までは再現せず、その時点の目標状態から始まる。
        /// 録画長以上を指定すると最終状態を適用して即座に完了する。負値・NaN・無限大は警告して false を返す。
        /// </summary>
        public bool StartPlayback(double startOffsetSeconds)
        {
            // 録画停止などの副作用より前に弾く。
            if (!RecPlaybackScheduler.IsValidStartOffset(startOffsetSeconds))
            {
                UnityEngine.Debug.LogWarning($"REC playback start was ignored because startOffsetSeconds ({startOffsetSeconds}) must be a finite, non-negative number.");
                return false;
            }

            if (!TryEnsureReady(out FacialController controller, out FacialProfile profile))
            {
                return false;
            }

            StopRecording();
            EnsurePlaybackSession(controller);

            if (_loadedTimeline != null)
            {
                if (_playbackUseCase.Load(_loadedTimeline, profile) == null)
                {
                    return false;
                }
            }

            return _playbackUseCase.StartPlayback(startOffsetSeconds);
        }

        public void StopPlayback()
        {
            _playbackUseCase?.StopPlayback();
        }

        /// <summary>
        /// 引数なしの Load が読むテイク名。<see cref="RecordingName"/> が直近の録画で要求した名前と同じなら、
        /// 実際に保存した名前（連番付与後）を返す。<see cref="RecordingName"/> が空なら null（直近のテイク）。
        /// </summary>
        private string ResolveNameForLoad()
        {
            if (string.IsNullOrWhiteSpace(_defaultRecordingName))
            {
                return null;
            }

            string name = _defaultRecordingName.Trim();
            return string.Equals(name, _lastRequestedRecordingName, StringComparison.Ordinal)
                ? _lastRecordingName
                : name;
        }

        /// <summary>
        /// 読み込みに失敗したとき、前に読み込んだテイクを捨てる。再生セッションごと破棄するので、
        /// 次の <see cref="StartPlayback"/> は「未読み込み」として警告し、古いテイクを再生しない。
        /// </summary>
        private void DiscardLoadedRecording()
        {
            _loadedTimeline = null;
            _loadedRecordingName = null;
            _loadedRecordingPath = null;
            DisposePlaybackSession();
        }

        private void Update()
        {
            // オープン失敗はライタースレッドで判明するので、気づいた時点で録画を止めて警告する。
            if (_streamWriter != null && !SyncRecordingOutput())
            {
                DisposeRecordingSession();
            }

            if (_playbackUseCase == null || _playbackUseCase.State != RecPlaybackState.Playing)
            {
                return;
            }

            _playbackUseCase.Tick(Time.deltaTime);
        }

        private void OnValidate()
        {
            if (!RecordingUseCase.IsValidStartOffset(_recordingStartOffsetSeconds))
            {
                _recordingStartOffsetSeconds = 0d;
            }
        }

        private void OnDisable()
        {
            StopSession();
        }

        private void OnDestroy()
        {
            StopSession();
            DisposePlaybackSession();
        }

        private double ResolveStartOffsetSeconds()
        {
            double offsetSeconds = _recordingStartOffsetSeconds;
            if (RecordingUseCase.IsValidStartOffset(offsetSeconds))
            {
                return offsetSeconds;
            }

            // シリアライズ値を直接書き換えた場合など、setter / OnValidate を通らない不正値だけがここに来る。
            UnityEngine.Debug.LogWarning($"REC recording start offset {offsetSeconds} was invalid and was treated as 0.");
            return 0d;
        }

        private void StopSession()
        {
            StopRecording();
            StopPlayback();
        }

        private void EnsurePlaybackSession(FacialController controller)
        {
            if (controller == null)
            {
                return;
            }

            if (ReferenceEquals(_runtimeController, controller)
                && _playbackUseCase != null
                && _analogInjector != null
                && _triggerInjector != null)
            {
                return;
            }

            DisposePlaybackSession();

            _runtimeController = controller;
            _analogInjector = new RecAnalogInjector(controller.InputSourceRegistry);
            _triggerInjector = new RecTriggerInjector(
                id => controller.TryGetExpressionTriggerSourceById(id, out ExpressionTriggerInputSourceBase source)
                    ? source
                    : null,
                () => CollectTriggerSources(controller.InputSourceRegistry));
            _expressionInjector = new RecExpressionInjector(() => controller.ExpressionActivationGate);
            _valueProviderInjector = new RecValueProviderInjector(
                controller.InputSourceRegistry,
                () => controller.BlendShapeCount);
            _playbackUseCase = new PlaybackUseCase(
                _triggerInjector,
                _expressionInjector,
                _analogInjector,
                _valueProviderInjector);
            _playbackUseCase.Completed += HandlePlaybackCompleted;
        }

        private void DisposePlaybackSession()
        {
            if (_playbackUseCase != null)
            {
                _playbackUseCase.Completed -= HandlePlaybackCompleted;
                _playbackUseCase.StopPlayback();
                _playbackUseCase = null;
            }

            _analogInjector = null;
            _triggerInjector = null;
            _expressionInjector = null;
            _valueProviderInjector = null;
            _runtimeController = null;
        }

        private void DisposeRecordingSession()
        {
            if (_recordingUseCase != null)
            {
                _recordingUseCase.StopRecording();
                _recordingUseCase.Dispose();
                _recordingUseCase = null;
            }

            if (_streamWriter != null)
            {
                _streamWriter.Dispose();
                if (!SyncRecordingOutput())
                {
                    UnityEngine.Debug.LogWarning($"REC recording was discarded because the output file could not be created: {_requestedRecordingPath}");
                }

                _streamWriter = null;
            }

            _requestedRecordingName = null;
            _requestedRecordingPath = null;
        }

        /// <summary>
        /// ライタースレッドが開いた出力ファイルのパスを <see cref="LastRecordingName"/> / <see cref="LastRecordingPath"/>
        /// に反映する。出力ファイルを開けなかったセッションなら false を返す（前回のテイクの値は残す）。
        /// </summary>
        private bool SyncRecordingOutput()
        {
            if (_streamWriter.HasOutputFailed)
            {
                return false;
            }

            string outputFilePath = _streamWriter.OutputFilePath;
            if (outputFilePath == null || string.Equals(outputFilePath, _lastRecordingPath, StringComparison.Ordinal))
            {
                return true;
            }

            _lastRecordingPath = outputFilePath;
            _lastRecordingName = Path.GetFileNameWithoutExtension(outputFilePath);
            _lastRequestedRecordingName = _requestedRecordingName;
            if (!string.Equals(outputFilePath, _requestedRecordingPath, StringComparison.Ordinal))
            {
                UnityEngine.Debug.Log($"REC recording '{Path.GetFileNameWithoutExtension(_requestedRecordingPath)}' already exists. Saving as '{_lastRecordingName}' instead.");
            }

            return true;
        }

        private void HandlePlaybackCompleted()
        {
            Completed?.Invoke();
        }

        private bool TryEnsureReady(out FacialController controller, out FacialProfile profile)
        {
            controller = _facialController != null
                ? _facialController
                : GetComponent<FacialController>();
            profile = default;

            if (controller == null)
            {
                UnityEngine.Debug.LogWarning("REC operation was ignored because FacialController was not assigned.");
                return false;
            }

            _facialController = controller;

            if (!controller.IsInitialized || !controller.CurrentProfile.HasValue)
            {
                UnityEngine.Debug.LogWarning("REC operation was ignored because FacialController was not initialized.");
                return false;
            }

            if (controller.InputObservationBus == null)
            {
                UnityEngine.Debug.LogWarning("REC operation was ignored because the FacialController input observation bus was unavailable.");
                return false;
            }

            if (controller.InputSourceRegistry == null)
            {
                UnityEngine.Debug.LogWarning("REC operation was ignored because the FacialController input source registry was unavailable.");
                return false;
            }

            profile = controller.CurrentProfile.Value;
            return true;
        }

        private static string ResolveAssetName(FacialController controller)
        {
            FacialCharacterProfileSO character = controller.CharacterSO;
            if (character != null && !string.IsNullOrWhiteSpace(character.CharacterAssetName))
            {
                return character.CharacterAssetName;
            }

            return controller.gameObject.name;
        }

        private static void GetQueueCapacities(int blendShapeCount, out int floatCapacity, out int byteCapacity)
        {
            int safeCount = Math.Max(0, blendShapeCount);
            int maskByteCount = (safeCount + 7) / 8;
            floatCapacity = Math.Max(128, checked(4 * safeCount));
            byteCapacity = Math.Max(64, checked(4 * maskByteCount));
        }

        private static IReadOnlyList<ExpressionTriggerInputSourceBase> CollectTriggerSources(IInputSourceRegistry registry)
        {
            var triggerSources = new List<ExpressionTriggerInputSourceBase>();
            IReadOnlyList<string> registeredIds = registry.RegisteredIds ?? Array.Empty<string>();
            for (int i = 0; i < registeredIds.Count; i++)
            {
                if (!registry.TryResolve(registeredIds[i], out IInputSource source))
                {
                    continue;
                }

                if (source is ExpressionTriggerInputSourceBase triggerSource)
                {
                    triggerSources.Add(triggerSource);
                }
            }

            return triggerSources;
        }
    }
}
