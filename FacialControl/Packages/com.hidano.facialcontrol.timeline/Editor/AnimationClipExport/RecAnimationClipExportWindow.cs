using System;
using System.IO;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
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
    /// <para>入力は REC ファイル / 対象キャラ（シーン上の FacialController か prefab アセット。HID-417）/ Profile（既定はキャラの Character SO）/ 入力源・レイヤーの
    /// 対応表 / 出力フォルダ。TimelineAsset・Bake・PlayableDirector の配線は不要で、シーンも書き換えない
    /// （<see cref="RecAnimationClipExporter"/>）。</para>
    /// <para>出力は AnimationTrack 変換と同じく、BlendShape の担当レイヤーごとの Clip + Base + Gaze（HID-410）。Split Clips By で
    /// BlendShape 単位、Frame Rate で書き出しのフレームレートを選べる（HID-422）。Clip の値は合成後の最終値なので、
    /// 編集しても FacialControl のレイヤー合成は再計算されない。</para>
    /// <para>REC は選んだとき（Browse、またはパスの入力を確定したとき）に読み、読み込んだ内容と識別子（ハッシュ）を保持して書き出しに使う。
    /// キャラ・Profile の変更では読み直さない。書き出し時に REC 欄のパスかファイルが読み込み時から変わっていれば読み直し、
    /// 対応表で選んでいた対応先が読み直しで元に戻ったときは書き出さずに知らせる（HID-423）。</para>
    /// </remarks>
    public sealed class RecAnimationClipExportWindow : EditorWindow
    {
        internal const string DefaultOutputFolder = "Assets";

        private TextField _recordingPathField;
        private ObjectField _controllerField;
        private ObjectField _profileField;
        private TextField _outputFolderField;
        private Toggle _createTimelineToggle;
        private FacialAnimationClipExportOptionsFields _optionsFields;
        private RecSourceRemapPanel _remapPanel;
        private HelpBox _statusBox;
        private bool _built;

        // 読み込んだ REC（Reload のたびに更新する）。
        private RecBinaryFormat.ReadResult _loadedRecording;
        private string _loadedPath;
        private string _loadedKey;
        private long _loadedLength;
        private DateTime _loadedWriteTimeUtc;

        // キャラの Character SO から自動で入れた Profile（ユーザーが選んだものではない）。null なら自動ではない。
        private FacialCharacterProfileSO _autoProfile;

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

        /// <summary>テスト用: Profile 欄の値。</summary>
        internal FacialCharacterProfileSO ProfileValue => _profileField != null ? _profileField.value as FacialCharacterProfileSO : null;

        /// <summary>テスト用: 直前の書き出し結果（失敗・例外のときは null）。</summary>
        internal RecAnimationClipExportResult LastResult { get; private set; }

        /// <summary>テスト用: REC を読み込んだ回数。</summary>
        internal int RecordingLoadCount { get; private set; }

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

            // 結果や警告が長くなっても見切れないよう、Export ボタン以外はスクロールさせる。
            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexGrow = 1f;
            scroll.contentContainer.style.paddingLeft = 10f;
            scroll.contentContainer.style.paddingRight = 10f;
            scroll.contentContainer.style.paddingTop = 4f;
            scroll.contentContainer.style.paddingBottom = 10f;
            root.Add(scroll);
            VisualElement content = scroll.contentContainer;

            content.Add(CreateSectionHeader("Input"));

            // パスを打ち込んだときは確定（Enter・フォーカス外れ）で読み込む。1 文字ごとには読まない。
            _recordingPathField = new TextField("REC File") { isDelayed = true };
            _recordingPathField.RegisterValueChangedCallback(_ => Reload());
            content.Add(CreatePathRow(_recordingPathField, BrowseRecording));

            _controllerField = new ObjectField("Character")
            {
                objectType = typeof(FacialController),
                allowSceneObjects = true,
                tooltip = "書き出し対象の FacialController。シーン上のものか、prefab アセット（Project ウィンドウからドラッグ）を指定する" +
                    "（BlendShape 名・renderer・目ボーンの取得元。どちらも書き換えない）",
            };
            _controllerField.RegisterValueChangedCallback(evt => OnControllerChanged(evt.newValue as FacialController));
            AddField(content, _controllerField);

            _profileField = new ObjectField("Profile")
            {
                objectType = typeof(FacialCharacterProfileSO),
                allowSceneObjects = false,
                tooltip = "合成に使う Profile（空ならキャラの Character SO）",
            };
            _profileField.RegisterValueChangedCallback(_ =>
            {
                // ユーザーが選んだ Profile はキャラを変えても差し替えない。
                _autoProfile = null;
                RebuildRemap();
            });
            AddField(content, _profileField);

            _remapPanel = new RecSourceRemapPanel();
            content.Add(_remapPanel);

            content.Add(CreateSectionHeader("Output"));

            _outputFolderField = new TextField("Output Folder") { value = DefaultOutputFolder };
            content.Add(CreatePathRow(_outputFolderField, BrowseOutputFolder));

            _optionsFields = new FacialAnimationClipExportOptionsFields();
            foreach (VisualElement field in _optionsFields.Children())
            {
                field.style.marginBottom = FieldSpacing;
            }

            content.Add(_optionsFields);

            _createTimelineToggle = new Toggle("Also Create TimelineAsset")
            {
                value = false,
                tooltip = "書き出した Clip を AnimationTrack に並べた TimelineAsset も作る（Animator の Bind は Timeline で行う）",
            };
            AddField(content, _createTimelineToggle);

            _statusBox = new HelpBox("REC ファイルとキャラを選んでください。", HelpBoxMessageType.Info);
            _statusBox.style.marginTop = 12f;
            content.Add(_statusBox);

            var exportButton = new Button(() => Export()) { text = "Export AnimationClips" };
            exportButton.style.height = 28f;
            exportButton.style.marginLeft = 10f;
            exportButton.style.marginRight = 10f;
            exportButton.style.marginTop = 6f;
            exportButton.style.marginBottom = 10f;
            root.Add(exportButton);
        }

        private const float FieldSpacing = 4f;

        private static Label CreateSectionHeader(string text)
        {
            var header = new Label(text);
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 10f;
            header.style.marginBottom = 4f;
            header.style.paddingBottom = 2f;
            header.style.borderBottomWidth = 1f;
            header.style.borderBottomColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            return header;
        }

        private static void AddField(VisualElement parent, VisualElement field)
        {
            field.style.marginBottom = FieldSpacing;
            parent.Add(field);
        }

        /// <summary>パス欄の右に「Browse...」ボタンを並べた行。</summary>
        private static VisualElement CreatePathRow(TextField field, Action browse)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = FieldSpacing;
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            row.Add(field);
            var button = new Button(browse) { text = "Browse..." };
            button.style.width = 80f;
            row.Add(button);
            return row;
        }

        /// <summary>
        /// 入力欄を設定して REC を読み込む（テストとプログラムからの利用向け）。<paramref name="profile"/> が null ならキャラの Character SO を
        /// 自動で入れる（キャラを変えると差し替わる）。<paramref name="options"/> を省略すると既定（レイヤー単位・Timeline のフレームレート）にする
        /// （前回 UI で選んだ値は使わない）。
        /// </summary>
        internal void SetInputs(
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profile,
            string outputFolder,
            bool createTimeline = false,
            FacialAnimationClipExportOptions options = default)
        {
            EnsureBuilt();
            _recordingPathField.SetValueWithoutNotify(recordingPath ?? string.Empty);
            _controllerField.SetValueWithoutNotify(controller);
            if (profile != null)
            {
                _profileField.SetValueWithoutNotify(profile);
                _autoProfile = null;
            }
            else
            {
                SetAutoProfile(controller);
            }

            _outputFolderField.SetValueWithoutNotify(outputFolder ?? DefaultOutputFolder);
            _createTimelineToggle.SetValueWithoutNotify(createTimeline);
            _optionsFields.SetOptions(options);
            Reload();
        }

        /// <summary>キャラを選ぶ（テストとプログラムからの利用向け。UI でキャラを変えたときと同じ）。</summary>
        internal void SetController(FacialController controller)
        {
            EnsureBuilt();
            _controllerField.SetValueWithoutNotify(controller);
            OnControllerChanged(controller);
        }

        /// <summary>Profile を選ぶ（テストとプログラムからの利用向け。UI で Profile を選んだときと同じ）。</summary>
        internal void SetProfile(FacialCharacterProfileSO profile)
        {
            EnsureBuilt();
            _profileField.SetValueWithoutNotify(profile);
            _autoProfile = null;
            RebuildRemap();
        }

        /// <summary>REC を読み込み、対応表を作り直す。</summary>
        internal void Reload()
        {
            EnsureBuilt();
            string path = _recordingPathField.value;
            _loadedRecording = null;
            _loadedPath = null;
            _loadedKey = null;
            if (string.IsNullOrWhiteSpace(path))
            {
                RebuildRemap();
                SetStatus("REC ファイルを選んでください。", HelpBoxMessageType.Info);
                return;
            }

            RecordingLoadCount++;
            if (!RecFileReader.TryRead(path, out RecBinaryFormat.ReadResult readResult, out byte[] recordingBytes))
            {
                RebuildRemap();
                SetStatus("REC を読み込めませんでした（詳細は Console）。", HelpBoxMessageType.Error);
                return;
            }

            _loadedRecording = readResult;
            _loadedPath = path;
            _loadedKey = RecTimelineExportWindow.HashRecording(recordingBytes);
            ReadFileStamp(path, out _loadedLength, out _loadedWriteTimeUtc);
            RebuildRemap();
            SetStatus(ProfileValue == null ? "Profile（またはキャラ）を選んでください。" : "REC を読み込みました。", HelpBoxMessageType.Info);
        }

        /// <summary>書き出す（テストとプログラムからの利用向け）。</summary>
        internal bool Export()
        {
            EnsureBuilt();
            LastResult = null;

            // 対応表は読み込んだ REC に対して作ったもの。REC 欄やファイルの中身が読み込み時と違えば読み直す。
            // 読み直しで選んでいた対応先が消えたときは、前の REC 用の選択を当てずに止めて選び直してもらう。
            if (_loadedRecording == null || !IsLoadedRecordingCurrent())
            {
                bool hadChoices = _loadedRecording != null && !_remapPanel.BuildSourceRemap().IsIdentity;
                Reload();
                if (_loadedRecording == null)
                {
                    return false;
                }

                if (hadChoices && _remapPanel.BuildSourceRemap().IsIdentity)
                {
                    _remapPanel.Expand();
                    SetStatus(
                        "REC が読み込み時から変わっていたので読み直しました。Source / Layer Remap の選択が元に戻ったので、" +
                        "確認してからもう一度書き出してください。",
                        HelpBoxMessageType.Warning);
                    return false;
                }
            }

            if (_remapPanel.HasErrors)
            {
                SetStatus("入力源・レイヤーの対応表に誤りがあるため書き出せません。Source / Layer Remap を確認してください。", HelpBoxMessageType.Error);
                return false;
            }

            RecSourceRemap remap = _remapPanel.BuildSourceRemap();
            RecAnimationClipExportResult result;
            bool success;
            try
            {
                success = RecAnimationClipExporter.TryExport(
                    _loadedRecording,
                    _loadedPath,
                    _controllerField.value as FacialController,
                    ProfileValue,
                    remap.IsIdentity ? null : remap,
                    _outputFolderField.value,
                    _createTimelineToggle.value,
                    out result,
                    _optionsFields.Options);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                SetStatus($"書き出し中にエラーが発生しました（保存したアセットは削除しました）: {ex.Message}", HelpBoxMessageType.Error);
                return false;
            }

            if (!success)
            {
                SetStatus(result.Error, HelpBoxMessageType.Error);
                return false;
            }

            LastResult = result;
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

        /// <summary>
        /// REC 欄のパスと、そのファイルが読み込み時と同じかを返す。ファイルのサイズ・更新時刻が同じなら読み直さず、
        /// 違うときだけ中身のハッシュを取り直して比べる。
        /// </summary>
        private bool IsLoadedRecordingCurrent()
        {
            string path = _recordingPathField.value;
            if (!string.Equals(path, _loadedPath, StringComparison.Ordinal))
            {
                return false;
            }

            if (ReadFileStamp(path, out long length, out DateTime writeTimeUtc)
                && length == _loadedLength
                && writeTimeUtc == _loadedWriteTimeUtc)
            {
                return true;
            }

            return string.Equals(RecTimelineExportWindow.ComputeRecordingKey(path), _loadedKey, StringComparison.Ordinal);
        }

        private static bool ReadFileStamp(string path, out long length, out DateTime writeTimeUtc)
        {
            try
            {
                var info = new FileInfo(path);
                if (info.Exists)
                {
                    length = info.Length;
                    writeTimeUtc = info.LastWriteTimeUtc;
                    return true;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is NotSupportedException || ex is ArgumentException)
            {
            }

            length = -1;
            writeTimeUtc = default;
            return false;
        }

        private void OnControllerChanged(FacialController controller)
        {
            // Profile が空か、前のキャラから自動で入れたものなら、新しいキャラの Character SO に差し替える（REC は読み直さない）。
            FacialCharacterProfileSO current = ProfileValue;
            if (current == null || (_autoProfile != null && current == _autoProfile))
            {
                SetAutoProfile(controller);
                if (ProfileValue != current)
                {
                    RebuildRemap();
                    return;
                }
            }

            UpdateProfileNotice();
        }

        private void SetAutoProfile(FacialController controller)
        {
            FacialCharacterProfileSO profile = controller != null ? controller.CharacterSO : null;
            _profileField.SetValueWithoutNotify(profile);
            _autoProfile = profile;
        }

        /// <summary>保持している REC と現在の Profile で対応表を作り直す（REC は読み直さない）。</summary>
        private void RebuildRemap()
        {
            FacialCharacterProfileSO profileAsset = ProfileValue;
            _remapPanel.Rebuild(
                _loadedKey,
                _loadedRecording?.Timeline,
                profileAsset != null ? TimelineProfileSource.Resolve(profileAsset) : (FacialProfile?)null,
                profileAsset != null ? profileAsset.GetInstanceID() : 0);
            UpdateProfileNotice();
        }

        private void UpdateProfileNotice()
        {
            var controller = _controllerField.value as FacialController;
            FacialCharacterProfileSO profile = ProfileValue;
            if (controller != null && profile != null && controller.CharacterSO != null && controller.CharacterSO != profile)
            {
                _remapPanel.SetNotice(
                    $"Profile '{profile.name}' はキャラ '{controller.name}' の Character SO（'{controller.CharacterSO.name}'）と違います。" +
                    "合成はこの Profile で行い、BlendShape・目ボーンはキャラのものを使います。");
            }
            else
            {
                _remapPanel.SetNotice(null);
            }
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

            // 同じパスを選び直したときも読み直す（値が変わらないと変更通知が来ない）。
            _recordingPathField.SetValueWithoutNotify(selected);
            Reload();
        }

        private void BrowseOutputFolder()
        {
            // 今の出力フォルダから開く（無ければ Assets）。
            string current = RecAnimationClipExporter.NormalizeFolder(_outputFolderField.value);
            string initialDirectory = current != null && AssetDatabase.IsValidFolder(current)
                ? Path.GetFullPath(current)
                : UnityEngine.Application.dataPath;
            string selected = EditorUtility.OpenFolderPanel("Select Output Folder", initialDirectory, string.Empty);
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

        private void SetStatus(string message, HelpBoxMessageType messageType)
        {
            _statusBox.messageType = messageType;
            _statusBox.text = message;
        }
    }
}
