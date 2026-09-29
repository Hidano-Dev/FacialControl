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
        private RecordingUseCase _recordingUseCase;
        private RecStreamWriter _streamWriter;
        private RecTimeline _loadedTimeline;
        private string _loadedRecordingName;
        private string _loadedRecordingPath;
        private string _lastRecordingName;
        private string _lastRecordingPath;
        private string _requestedRecordingPath;

        public bool IsRecording => _recordingUseCase != null && _recordingUseCase.IsRecording;

        public RecordingState RecordingState => _recordingUseCase?.State ?? RecordingState.Idle;

        public RecPlaybackState PlaybackState => _playbackUseCase?.State ?? RecPlaybackState.Idle;

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
        /// 録画中に差し替えても次の録画から反映される。契約は <see cref="IRecClock"/> を参照。
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
                if (!RecOffsetClock.IsValidOffset(value))
                {
                    throw new ArgumentOutOfRangeException(nameof(value), "Recording start offset must be a finite, non-negative number of seconds.");
                }

                _recordingStartOffsetSeconds = value;
            }
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

            // 出力先の予約（同名衝突時の連番付与）とファイルのオープンは RecStreamWriter がライタースレッドで行う。
            // 結果（実際のパス / オープン失敗）は Update と StopRecording で SyncRecordingOutput が拾う。
            RecBaselineState baseline = CaptureBaseline(profile, controller.InputSourceRegistry);
            _requestedRecordingPath = requestedFilePath;
            _streamWriter = new RecStreamWriter(requestedFilePath);
            _recordingUseCase = new RecordingUseCase(
                controller.InputObservationBus,
                CreateRecordingClock(),
                _streamWriter);
            _recordingUseCase.StartRecording(baseline);
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

            if (string.IsNullOrWhiteSpace(recordingName))
            {
                recordingName = _lastRecordingName;
                if (string.IsNullOrWhiteSpace(recordingName))
                {
                    UnityEngine.Debug.LogWarning("REC load was ignored because no recording name was given and nothing has been recorded yet.");
                    return false;
                }
            }

            StopRecording();
            StopPlayback();

            string assetName = ResolveAssetName(controller);
            if (!RecSidecarPath.TryBuildRecordingFilePath(assetName, recordingName, out string filePath, out string error))
            {
                UnityEngine.Debug.LogWarning($"REC load was ignored because the input path was invalid: {error}");
                return false;
            }

            if (!RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result))
            {
                return false;
            }

            EnsurePlaybackSession(controller);

            _loadedTimeline = result.Timeline;
            _loadedRecordingName = recordingName;
            _loadedRecordingPath = filePath;
            return _playbackUseCase.Load(_loadedTimeline, profile) != null;
        }

        public bool StartPlayback()
        {
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

            return _playbackUseCase.StartPlayback();
        }

        public void StopPlayback()
        {
            _playbackUseCase?.StopPlayback();
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
            if (!RecOffsetClock.IsValidOffset(_recordingStartOffsetSeconds))
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

        private IRecClock CreateRecordingClock()
        {
            IRecClock clock = RecordingClock ?? new RecStopwatchClock();
            double offsetSeconds = _recordingStartOffsetSeconds;
            if (!RecOffsetClock.IsValidOffset(offsetSeconds))
            {
                // シリアライズ値を直接書き換えた場合など、setter / OnValidate を通らない不正値だけがここに来る。
                UnityEngine.Debug.LogWarning($"REC recording start offset {offsetSeconds} was invalid and was treated as 0.");
                return clock;
            }

            return offsetSeconds > 0d ? new RecOffsetClock(clock, offsetSeconds) : clock;
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
            _playbackUseCase = new PlaybackUseCase(_triggerInjector, _analogInjector);
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

        private static RecBaselineState CaptureBaseline(FacialProfile profile, IInputSourceRegistry registry)
        {
            var triggerEntries = new List<RecBaselineState.TriggerEntry>();
            var analogEntries = new List<RecBaselineState.AnalogEntry>();

            IReadOnlyList<string> registeredIds = registry.RegisteredIds ?? Array.Empty<string>();
            for (int i = 0; i < registeredIds.Count; i++)
            {
                string sourceId = registeredIds[i];
                if (!registry.TryResolve(sourceId, out IInputSource source) || source == null)
                {
                    continue;
                }

                if (source is ExpressionTriggerInputSourceBase triggerSource)
                {
                    IReadOnlyList<string> activeExpressionIds = triggerSource.ActiveExpressionIds;
                    if (activeExpressionIds.Count > 0)
                    {
                        triggerEntries.Add(new RecBaselineState.TriggerEntry(sourceId, activeExpressionIds));
                    }

                    continue;
                }

                if (source is not IAnalogInputSource analogSource
                    || !analogSource.IsValid
                    || analogSource.AxisCount <= 0)
                {
                    continue;
                }

                var axes = new float[analogSource.AxisCount];
                if (!analogSource.TryReadAxes(axes))
                {
                    continue;
                }

                analogEntries.Add(new RecBaselineState.AnalogEntry(sourceId, axes));
            }

            return new RecBaselineState(triggerEntries, analogEntries);
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
