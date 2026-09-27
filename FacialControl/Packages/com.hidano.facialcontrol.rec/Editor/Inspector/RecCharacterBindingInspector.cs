using Hidano.FacialControl.Rec.Adapters.Playable;
using Hidano.FacialControl.Rec.Application.UseCases;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Rec.Editor.Inspector
{
    [CustomEditor(typeof(RecCharacterBinding))]
    public sealed class RecCharacterBindingInspector : UnityEditor.Editor
    {
        public const string EditModeHelpBoxName = "rec-binding-edit-mode-help";
        public const string RecordingNameFieldName = "rec-binding-recording-name-field";
        public const string StartRecordingButtonName = "rec-binding-start-recording-button";
        public const string StopRecordingButtonName = "rec-binding-stop-recording-button";
        public const string LoadRecordingButtonName = "rec-binding-load-recording-button";
        public const string StartPlaybackButtonName = "rec-binding-start-playback-button";
        public const string StopPlaybackButtonName = "rec-binding-stop-playback-button";
        public const string RecordingStateLabelName = "rec-binding-recording-state-label";
        public const string PlaybackStateLabelName = "rec-binding-playback-state-label";
        public const string ElapsedSecondsLabelName = "rec-binding-elapsed-seconds-label";
        public const string PathLabelName = "rec-binding-path-label";

        public const string RecordingStateLabelFormat = "Recording: {0}";
        public const string PlaybackStateLabelFormat = "Playback: {0}";
        public const string ElapsedSecondsLabelFormat = "Elapsed Seconds: {0:F3}";
        public const string PathLabelFormat = "Path: {0}";
        public const string EmptyPathText = "---";

        public override VisualElement CreateInspectorGUI()
        {
            var binding = (RecCharacterBinding)target;
            var root = new VisualElement();

            root.Add(new PropertyField(serializedObject.FindProperty("_facialController")));
            root.Add(new PropertyField(serializedObject.FindProperty("_defaultRecordingName")));

            var editModeHelp = new HelpBox(
                "Play モード中のみ記録/再生ボタンを操作できます。",
                HelpBoxMessageType.Info)
            {
                name = EditModeHelpBoxName,
            };
            root.Add(editModeHelp);

            // Default Recording Name で埋めない。空欄は「既定名に委ねる」の意味で、Start は
            // Default Recording Name → タイムスタンプ、Load は直近に録画したテイクへフォールバックする。
            var recordingNameField = new TextField("Recording Name")
            {
                name = RecordingNameFieldName,
                tooltip = "Start Recording: 空のままなら Default Recording Name、それも空なら take-yyyyMMdd-HHmmss で命名する。同名の録画がある場合は上書きせず -2, -3… を付与して保存する。\nLoad Recording: 空のままなら直近に録画したテイクを読み込む。",
            };
            root.Add(recordingNameField);

            // Path 表示は「最後に操作した対象」を優先する（録画開始 → 録画パス、Load → 読み込みパス）。
            bool preferRecordingPath = false;
            string seenRecordingPath = binding.LastRecordingPath;
            string seenLoadedPath = binding.LoadedRecordingPath;

            var recordButtons = new VisualElement();
            recordButtons.style.flexDirection = FlexDirection.Row;

            var startRecordingButton = new Button(() =>
            {
                preferRecordingPath = true;
                binding.StartRecording(ResolveRecordingName(recordingNameField.value));
            })
            {
                name = StartRecordingButtonName,
                text = "Start Recording",
            };
            var stopRecordingButton = new Button(binding.StopRecording)
            {
                name = StopRecordingButtonName,
                text = "Stop Recording",
            };
            startRecordingButton.style.marginRight = 4;
            recordButtons.Add(startRecordingButton);
            recordButtons.Add(stopRecordingButton);
            root.Add(recordButtons);

            var playbackButtons = new VisualElement();
            playbackButtons.style.flexDirection = FlexDirection.Row;

            var loadRecordingButton = new Button(() =>
            {
                preferRecordingPath = false;
                binding.LoadRecording(ResolveRecordingName(recordingNameField.value));
            })
            {
                name = LoadRecordingButtonName,
                text = "Load Recording",
            };
            var startPlaybackButton = new Button(() => binding.StartPlayback())
            {
                name = StartPlaybackButtonName,
                text = "Start Playback",
            };
            var stopPlaybackButton = new Button(binding.StopPlayback)
            {
                name = StopPlaybackButtonName,
                text = "Stop Playback",
            };
            loadRecordingButton.style.marginRight = 4;
            startPlaybackButton.style.marginRight = 4;
            playbackButtons.Add(loadRecordingButton);
            playbackButtons.Add(startPlaybackButton);
            playbackButtons.Add(stopPlaybackButton);
            root.Add(playbackButtons);

            var recordingStateLabel = new Label { name = RecordingStateLabelName };
            var playbackStateLabel = new Label { name = PlaybackStateLabelName };
            var elapsedSecondsLabel = new Label { name = ElapsedSecondsLabelName };
            var pathLabel = new Label { name = PathLabelName };
            recordingStateLabel.style.marginTop = 4;
            playbackStateLabel.style.marginTop = 2;
            elapsedSecondsLabel.style.marginTop = 2;
            pathLabel.style.marginTop = 2;
            root.Add(recordingStateLabel);
            root.Add(playbackStateLabel);
            root.Add(elapsedSecondsLabel);
            root.Add(pathLabel);

            void Refresh()
            {
                bool isPlaying = EditorApplication.isPlaying;
                editModeHelp.style.display = isPlaying ? DisplayStyle.None : DisplayStyle.Flex;
                startRecordingButton.SetEnabled(isPlaying);
                stopRecordingButton.SetEnabled(isPlaying);
                loadRecordingButton.SetEnabled(isPlaying);
                startPlaybackButton.SetEnabled(isPlaying);
                stopPlaybackButton.SetEnabled(isPlaying);

                // スクリプト API 経由の操作も拾えるよう、パスの変化からも優先対象を更新する。
                if (!string.Equals(seenRecordingPath, binding.LastRecordingPath, System.StringComparison.Ordinal))
                {
                    seenRecordingPath = binding.LastRecordingPath;
                    preferRecordingPath = true;
                }

                if (!string.Equals(seenLoadedPath, binding.LoadedRecordingPath, System.StringComparison.Ordinal))
                {
                    seenLoadedPath = binding.LoadedRecordingPath;
                    preferRecordingPath = false;
                }

                recordingStateLabel.text = string.Format(RecordingStateLabelFormat, binding.IsRecording);
                playbackStateLabel.text = string.Format(PlaybackStateLabelFormat, binding.PlaybackState);
                elapsedSecondsLabel.text = string.Format(ElapsedSecondsLabelFormat, binding.ElapsedSeconds);
                pathLabel.text = string.Format(PathLabelFormat, ResolveDisplayPath(binding, preferRecordingPath));
            }

            Refresh();
            root.schedule.Execute(Refresh).Every(100);
            return root;
        }

        private static string ResolveRecordingName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ResolveDisplayPath(RecCharacterBinding binding, bool preferRecordingPath)
        {
            // 録画中、または最後の操作が録画なら、実際に保存したパス（連番付与後）を表示する。
            if ((binding.IsRecording || preferRecordingPath) && !string.IsNullOrWhiteSpace(binding.LastRecordingPath))
            {
                return binding.LastRecordingPath;
            }

            if (!string.IsNullOrWhiteSpace(binding.LoadedRecordingPath))
            {
                return binding.LoadedRecordingPath;
            }

            if (!string.IsNullOrWhiteSpace(binding.LastRecordingPath))
            {
                return binding.LastRecordingPath;
            }

            return EmptyPathText;
        }
    }
}
