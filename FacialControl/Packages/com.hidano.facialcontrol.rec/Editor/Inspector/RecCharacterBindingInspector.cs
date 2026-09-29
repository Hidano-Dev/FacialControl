using System.Collections.Generic;
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
        public const string RecordingDropdownName = "rec-binding-recording-dropdown";
        public const string RefreshRecordingsButtonName = "rec-binding-refresh-recordings-button";
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

            // Default Recording Name で埋めない。空欄は「既定名に委ねる」の意味で、
            // Default Recording Name → タイムスタンプへフォールバックする。
            var recordingNameField = new TextField("Recording Name")
            {
                name = RecordingNameFieldName,
                tooltip = "Start Recording で使うテイク名。空のままなら Default Recording Name、それも空なら take-yyyyMMdd-HHmmss で命名する。同名の録画がある場合は上書きせず -2, -3… を付与して保存する。",
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

            // Load 対象は保存済み録画からの選択式にする（テイク名の打ち間違いを起こさない）。
            // 一覧はファイル I/O を伴うので毎 Refresh では取らず、表示時・録画停止時・Refresh ボタンでだけ更新する。
            var recordingRow = new VisualElement();
            recordingRow.style.flexDirection = FlexDirection.Row;
            var recordingDropdown = new DropdownField("Load Target")
            {
                name = RecordingDropdownName,
                tooltip = "Load Recording で読み込むテイク。更新日時の新しい順に並ぶ。録画がまだ無ければ空で、その場合 Load Recording は直近に録画したテイクを読み込む。",
            };
            recordingDropdown.style.flexGrow = 1;
            var refreshRecordingsButton = new Button
            {
                name = RefreshRecordingsButtonName,
                text = "Refresh",
                tooltip = "保存済み録画の一覧を読み直す。",
            };
            recordingRow.Add(recordingDropdown);
            recordingRow.Add(refreshRecordingsButton);
            root.Add(recordingRow);

            void RefreshRecordingChoices(string preferredName)
            {
                // Play モード終了で録画が止まった直後は、binding がすでに破棄されていることがある。
                if (binding == null)
                {
                    return;
                }

                IReadOnlyList<string> names = binding.GetRecordingNames();
                recordingDropdown.choices = new List<string>(names);
                recordingDropdown.SetValueWithoutNotify(
                    ResolveRecordingSelection(names, preferredName, recordingDropdown.value));
            }

            refreshRecordingsButton.clicked += () => RefreshRecordingChoices(null);
            RefreshRecordingChoices(binding.LastRecordingName);
            bool wasRecording = binding.IsRecording;

            // Load / Start Playback は録画中なら暗黙に録画を止める。そのとき選択を新しいテイクへ動かすと、
            // 実際に読み込んだ・再生するテイクと表示がずれるので、一覧だけ読み直して選択は保つ。
            void InvokeKeepingSelection(System.Action action)
            {
                bool recordingBefore = binding.IsRecording;
                action();
                if (recordingBefore && !binding.IsRecording)
                {
                    wasRecording = false;
                    RefreshRecordingChoices(recordingDropdown.value);
                }
            }

            var playbackButtons = new VisualElement();
            playbackButtons.style.flexDirection = FlexDirection.Row;

            var loadRecordingButton = new Button(() =>
            {
                preferRecordingPath = false;
                string selectedName = ResolveRecordingName(recordingDropdown.value);
                InvokeKeepingSelection(() => binding.LoadRecording(selectedName));
            })
            {
                name = LoadRecordingButtonName,
                text = "Load Recording",
            };
            var startPlaybackButton = new Button(() => InvokeKeepingSelection(() => binding.StartPlayback()))
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

                // 録画停止直後は一覧を読み直し、保存したテイクを選択状態にする（スクリプト API 経由の停止も拾う）。
                bool isRecording = binding.IsRecording;
                if (wasRecording && !isRecording)
                {
                    RefreshRecordingChoices(binding.LastRecordingName);
                }

                wasRecording = isRecording;

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

        /// <summary>
        /// Load 対象ドロップダウンの選択値を決める。<paramref name="preferredName"/>（直前に保存したテイク等）→
        /// 現在の選択値 → 先頭（最新）の順で、一覧に存在するものを選ぶ。一覧が空なら空文字。
        /// </summary>
        public static string ResolveRecordingSelection(IReadOnlyList<string> names, string preferredName, string currentValue)
        {
            if (names == null || names.Count == 0)
            {
                return string.Empty;
            }

            if (Contains(names, preferredName))
            {
                return preferredName;
            }

            if (Contains(names, currentValue))
            {
                return currentValue;
            }

            return names[0];
        }

        private static bool Contains(IReadOnlyList<string> names, string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return false;
            }

            for (int i = 0; i < names.Count; i++)
            {
                if (string.Equals(names[i], value, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ResolveRecordingName(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ResolveDisplayPath(RecCharacterBinding binding, bool preferRecordingPath)
        {
            // 録画中は今のテイクのパスを表示する。ライタースレッドがファイルを開くまでは前回のテイクと
            // 取り違えないよう空表示にする。
            if (binding.IsRecording)
            {
                return binding.CurrentRecordingPath ?? EmptyPathText;
            }

            // 最後の操作が録画なら、実際に保存したパス（連番付与後）を表示する。
            if (preferRecordingPath && !string.IsNullOrWhiteSpace(binding.LastRecordingPath))
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
