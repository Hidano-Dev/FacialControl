using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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

        private FacialController _runtimeController;
        private PlaybackUseCase _playbackUseCase;
        private RecAnalogInjector _analogInjector;
        private RecTriggerInjector _triggerInjector;
        private RecordingUseCase _recordingUseCase;
        private RecStreamWriter _streamWriter;
        private RecTimeline _loadedTimeline;
        private string _loadedRecordingName;
        private string _loadedRecordingPath;
        private string _lastRecordingPath;

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

        public string LastRecordingPath => _lastRecordingPath;

        public string LoadedRecordingPath => _loadedRecordingPath;

        public event Action Completed;

        public FacialController FacialController
        {
            get => _facialController;
            set => _facialController = value;
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

            // 同名テイクは上書きせず連番を付与して保存する（REC データは代替が効かない）。
            string filePath = RecSidecarPath.ResolveUniqueFilePath(requestedFilePath);
            if (!string.Equals(filePath, requestedFilePath, StringComparison.Ordinal))
            {
                UnityEngine.Debug.Log($"REC recording '{resolvedRecordingName}' already exists. Saving as '{System.IO.Path.GetFileNameWithoutExtension(filePath)}' instead.");
            }

            RecBaselineState baseline = CaptureBaseline(profile, controller.InputSourceRegistry);
            _streamWriter = new RecStreamWriter(filePath);
            _recordingUseCase = new RecordingUseCase(
                controller.InputObservationBus,
                new StopwatchRecClock(),
                _streamWriter);
            _recordingUseCase.StartRecording(baseline);
            _lastRecordingPath = filePath;
            return true;
        }

        public void StopRecording()
        {
            DisposeRecordingSession();
        }

        public bool LoadRecording(string recordingName)
        {
            if (!TryEnsureReady(out FacialController controller, out FacialProfile profile))
            {
                return false;
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
            if (_playbackUseCase == null || _playbackUseCase.State != RecPlaybackState.Playing)
            {
                return;
            }

            _playbackUseCase.Tick(Time.deltaTime);
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
                _streamWriter = null;
            }
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

        private sealed class StopwatchRecClock : Hidano.FacialControl.Rec.Domain.Interfaces.IRecClock
        {
            private readonly Stopwatch _stopwatch = new Stopwatch();

            public double ElapsedSeconds => _stopwatch.Elapsed.TotalSeconds;

            public void Reset()
            {
                _stopwatch.Restart();
            }
        }
    }
}
