using System.IO;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Models;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.AnimationClipExport
{
    /// <summary>
    /// REC（.fcrec）から AnimationClip（.anim）を直接書き出すウィンドウ（HID-409）。
    /// </summary>
    /// <remarks>
    /// <para>入力は REC ファイル / 対象キャラ（シーン上の FacialController）/ Profile（既定はキャラの Character SO）/ 入力源・レイヤーの
    /// 対応表 / 出力フォルダ。TimelineAsset・Bake・PlayableDirector の配線は不要で、シーンも書き換えない
    /// （<see cref="RecAnimationClipExporter"/>）。</para>
    /// <para>出力は AnimationTrack 変換と同じく、BlendShape の担当レイヤーごとの Clip + Base + Gaze（HID-410）。Clip の値は合成後の最終値なので、
    /// 編集しても FacialControl のレイヤー合成は再計算されない。</para>
    /// </remarks>
    public sealed class RecAnimationClipExportWindow : EditorWindow
    {
        internal const string DefaultOutputFolder = "Assets";

        private TextField _recordingPathField;
        private ObjectField _controllerField;
        private ObjectField _profileField;
        private TextField _outputFolderField;
        private Toggle _createTimelineToggle;
        private RecSourceRemapPanel _remapPanel;
        private HelpBox _statusBox;
        private bool _built;

        [MenuItem("Tools/FacialControl/Timeline/REC Export To AnimationClip")]
        public static void Open()
        {
            RecAnimationClipExportWindow window = GetWindow<RecAnimationClipExportWindow>();
            window.titleContent = new GUIContent("REC To AnimationClip");
            window.minSize = new Vector2(520f, 420f);
        }

        /// <summary>テスト用: 対応表の部品。</summary>
        internal RecSourceRemapPanel RemapPanel => _remapPanel;

        /// <summary>テスト用: ステータス欄の文言。</summary>
        internal string StatusText => _statusBox != null ? _statusBox.text : string.Empty;

        /// <summary>テスト用: 直前の書き出し結果。</summary>
        internal RecAnimationClipExportResult LastResult { get; private set; }

        private void CreateGUI()
        {
            EnsureBuilt();
        }

        /// <summary>UI を構築する（冪等。ウィンドウを表示せずにテストから呼べる）。</summary>
        internal void EnsureBuilt()
        {
            if (_built)
            {
                return;
            }

            _built = true;
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10f;
            root.style.paddingRight = 10f;
            root.style.paddingTop = 10f;
            root.style.paddingBottom = 10f;

            _recordingPathField = new TextField("REC File");
            root.Add(_recordingPathField);

            var recordingButtons = new VisualElement();
            recordingButtons.style.flexDirection = FlexDirection.Row;
            recordingButtons.style.marginBottom = 6f;
            root.Add(recordingButtons);
            recordingButtons.Add(new Button(BrowseRecording) { text = "Browse REC" });
            var reloadButton = new Button(Reload) { text = "Reload Sources" };
            reloadButton.style.marginLeft = 6f;
            recordingButtons.Add(reloadButton);

            _controllerField = new ObjectField("Character")
            {
                objectType = typeof(FacialController),
                allowSceneObjects = true,
                tooltip = "書き出し対象のシーン上の FacialController（BlendShape 名・renderer・目ボーンの取得元。書き換えない）",
            };
            _controllerField.RegisterValueChangedCallback(evt =>
            {
                // Profile が未指定なら、キャラの Character SO を既定にする。
                if (_profileField.value == null && evt.newValue is FacialController controller && controller.CharacterSO != null)
                {
                    _profileField.SetValueWithoutNotify(controller.CharacterSO);
                }

                ReloadIfRecordingSelected();
            });
            root.Add(_controllerField);

            _profileField = new ObjectField("Profile")
            {
                objectType = typeof(FacialCharacterProfileSO),
                allowSceneObjects = false,
                tooltip = "合成に使う Profile（空ならキャラの Character SO）",
            };
            _profileField.RegisterValueChangedCallback(_ => ReloadIfRecordingSelected());
            root.Add(_profileField);

            var outputRow = new VisualElement();
            outputRow.style.flexDirection = FlexDirection.Row;
            _outputFolderField = new TextField("Output Folder") { value = DefaultOutputFolder };
            _outputFolderField.style.flexGrow = 1f;
            outputRow.Add(_outputFolderField);
            outputRow.Add(new Button(BrowseOutputFolder) { text = "..." });
            root.Add(outputRow);

            _createTimelineToggle = new Toggle("Also Create TimelineAsset")
            {
                value = false,
                tooltip = "書き出した Clip を AnimationTrack に並べた TimelineAsset も作る（Animator の Bind は Timeline で行う）",
            };
            root.Add(_createTimelineToggle);

            _remapPanel = new RecSourceRemapPanel();
            root.Add(_remapPanel);

            _statusBox = new HelpBox("REC ファイルとキャラを選んでください。", HelpBoxMessageType.Info);
            _statusBox.style.marginTop = 8f;
            root.Add(_statusBox);

            var exportButton = new Button(() => Export()) { text = "Export AnimationClips" };
            exportButton.style.marginTop = 8f;
            root.Add(exportButton);
        }

        /// <summary>入力欄を設定する（テストとプログラムからの利用向け）。<paramref name="profile"/> が null ならキャラの Character SO。</summary>
        internal void SetInputs(
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profile,
            string outputFolder,
            bool createTimeline = false)
        {
            EnsureBuilt();
            _recordingPathField.SetValueWithoutNotify(recordingPath ?? string.Empty);
            _controllerField.SetValueWithoutNotify(controller);
            _profileField.SetValueWithoutNotify(profile != null ? profile : controller != null ? controller.CharacterSO : null);
            _outputFolderField.SetValueWithoutNotify(outputFolder ?? DefaultOutputFolder);
            _createTimelineToggle.SetValueWithoutNotify(createTimeline);
            Reload();
        }

        /// <summary>REC を読み込み、対応表を作り直す。</summary>
        internal void Reload()
        {
            EnsureBuilt();
            string path = _recordingPathField.value;
            var profileAsset = _profileField.value as FacialCharacterProfileSO;
            if (string.IsNullOrWhiteSpace(path) || !RecFileReader.TryRead(path, out RecBinaryFormat.ReadResult readResult))
            {
                _remapPanel.Rebuild(null, null, null);
                SetStatus(string.IsNullOrWhiteSpace(path) ? "REC ファイルを選んでください。" : "REC を読み込めませんでした（詳細は Console）。",
                    string.IsNullOrWhiteSpace(path) ? HelpBoxMessageType.Info : HelpBoxMessageType.Error);
                return;
            }

            _remapPanel.Rebuild(
                Path.GetFullPath(path),
                readResult.Timeline,
                profileAsset != null ? TimelineProfileSource.Resolve(profileAsset) : (Hidano.FacialControl.Domain.Models.FacialProfile?)null);
            SetStatus(profileAsset == null ? "Profile（またはキャラ）を選んでください。" : "REC を読み込みました。", HelpBoxMessageType.Info);
        }

        /// <summary>書き出す（テストとプログラムからの利用向け）。</summary>
        internal bool Export()
        {
            EnsureBuilt();
            if (_remapPanel.HasErrors)
            {
                SetStatus("入力源・レイヤーの対応表に誤りがあるため書き出せません。Source / Layer Remap を確認してください。", HelpBoxMessageType.Error);
                return false;
            }

            RecSourceRemap remap = _remapPanel.BuildSourceRemap();
            bool success = RecAnimationClipExporter.TryExport(
                _recordingPathField.value,
                _controllerField.value as FacialController,
                _profileField.value as FacialCharacterProfileSO,
                remap.IsIdentity ? null : remap,
                _outputFolderField.value,
                _createTimelineToggle.value,
                out RecAnimationClipExportResult result);
            LastResult = result;
            if (!success)
            {
                SetStatus(result.Error, HelpBoxMessageType.Error);
                return false;
            }

            var message = new System.Text.StringBuilder();
            message.Append($"{result.Clips.Count} 本の AnimationClip を書き出しました。");
            for (int i = 0; i < result.Clips.Count; i++)
            {
                message.Append('\n').Append(result.Clips[i].AssetPath);
            }

            if (result.TimelineAssetPath != null)
            {
                message.Append('\n').Append(result.TimelineAssetPath).Append("（AnimationTrack に Animator を Bind して再生）");
            }

            if (result.RemapProblems.Count > 0)
            {
                message.Append("\n未対応（再生されない）: ").Append(result.RemapProblems.Count).Append(" 件。Console を確認してください。");
            }

            SetStatus(message.ToString(), result.RemapProblems.Count > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
            return true;
        }

        private void BrowseRecording()
        {
            string current = _recordingPathField.value;
            string initialDirectory = string.IsNullOrWhiteSpace(current) ? string.Empty : Path.GetDirectoryName(current);
            string selected = EditorUtility.OpenFilePanel("Select REC File", initialDirectory ?? string.Empty, "fcrec");
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            _recordingPathField.value = selected;
            Reload();
        }

        private void BrowseOutputFolder()
        {
            string selected = EditorUtility.OpenFolderPanel("Select Output Folder", UnityEngine.Application.dataPath, string.Empty);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            string folder = RecAnimationClipExporter.NormalizeFolder(selected);
            if (folder == null)
            {
                SetStatus("出力フォルダはプロジェクトの Assets 配下を選んでください。", HelpBoxMessageType.Error);
                return;
            }

            _outputFolderField.value = folder;
        }

        private void ReloadIfRecordingSelected()
        {
            if (_recordingPathField != null && !string.IsNullOrWhiteSpace(_recordingPathField.value))
            {
                Reload();
            }
        }

        private void SetStatus(string message, HelpBoxMessageType messageType)
        {
            _statusBox.messageType = messageType;
            _statusBox.text = message;
        }
    }
}
