using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    /// <para>REC は追加したとき（Add File / Add Folder / ドラッグ & ドロップ）に読み、読み込んだ内容と識別子（ハッシュ）を保持して書き出しに使う。
    /// キャラ・Profile の変更では読み直さない。書き出し時にファイルが読み込み時から変わっていれば読み直し、
    /// 対応表で選んでいた対応先が読み直しで元に戻ったときは書き出さずに知らせる（HID-423）。</para>
    /// <para>REC は複数並べて 1 回でまとめて書き出せる（HID-424）。対応表は先頭の REC を基準に 1 枚だけ作って全 REC に当て、
    /// 入力源・レイヤーの構成が先頭と違う REC があれば警告する（書き出しは止めない）。書き出しは REC ごとに行い、
    /// 1 本が失敗しても残りは書き出す。</para>
    /// </remarks>
    public sealed class RecAnimationClipExportWindow : EditorWindow
    {
        internal const string DefaultOutputFolder = "Assets";
        internal const string CompositionBoxName = "rec-clip-export-composition";

        private const string RecordingExtension = ".fcrec";

        private VisualElement _recordingList;
        private HelpBox _compositionBox;
        private ObjectField _controllerField;
        private ObjectField _profileField;
        private TextField _outputFolderField;
        private Toggle _createTimelineToggle;
        private FacialAnimationClipExportOptionsFields _optionsFields;
        private RecSourceRemapPanel _remapPanel;
        private HelpBox _statusBox;
        private bool _built;

        // 並べた REC（先頭が対応表の基準）。
        private readonly List<LoadedRecording> _recordings = new List<LoadedRecording>();

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

        /// <summary>テスト用: 直前の書き出しで先頭の REC の結果（どれか 1 本でも失敗・例外のときは null）。</summary>
        internal RecAnimationClipExportResult LastResult { get; private set; }

        /// <summary>テスト用: 直前の書き出しの REC ごとの結果（並べた順）。</summary>
        internal IReadOnlyList<RecAnimationClipBatchItem> LastResults { get; private set; } = Array.Empty<RecAnimationClipBatchItem>();

        /// <summary>テスト用: REC を読み込んだ回数。</summary>
        internal int RecordingLoadCount { get; private set; }

        /// <summary>テスト用: 並べた REC のパス（並べた順）。</summary>
        internal IReadOnlyList<string> RecordingPaths
        {
            get
            {
                var paths = new List<string>(_recordings.Count);
                for (int i = 0; i < _recordings.Count; i++)
                {
                    paths.Add(_recordings[i].Path);
                }

                return paths;
            }
        }

        /// <summary>テスト用: 先頭の REC と構成が違う REC の警告（無ければ空）。</summary>
        internal string CompositionWarningText =>
            _compositionBox != null && _compositionBox.style.display.value != DisplayStyle.None ? _compositionBox.text : string.Empty;

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

            content.Add(CreateRecordingSection());

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

            _statusBox = new HelpBox("REC ファイルを追加し、キャラを選んでください。", HelpBoxMessageType.Info);
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

        /// <summary>REC の一覧と追加・削除のボタン、構成の警告。一覧には Project / Explorer から .fcrec かフォルダをドロップできる。</summary>
        private VisualElement CreateRecordingSection()
        {
            var section = new VisualElement();
            section.style.marginBottom = FieldSpacing;

            var header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            var caption = new Label("REC Files");
            caption.style.flexGrow = 1f;
            caption.style.paddingLeft = 3f;
            header.Add(caption);
            header.Add(CreateSmallButton("Add File...", BrowseRecording, "REC（.fcrec）を 1 本追加する"));
            header.Add(CreateSmallButton("Add Folder...", BrowseRecordingFolder, "フォルダ直下の REC（.fcrec）をすべて追加する"));
            header.Add(CreateSmallButton("Clear", () => SetRecordings(Array.Empty<string>()), "一覧を空にする"));
            section.Add(header);

            _recordingList = new VisualElement { tooltip = "先頭の REC を基準に Source / Layer Remap を作る。.fcrec かフォルダをドロップして追加できる" };
            _recordingList.style.marginTop = 2f;
            _recordingList.style.minHeight = 24f;
            _recordingList.style.paddingLeft = 4f;
            _recordingList.style.paddingRight = 4f;
            _recordingList.style.paddingTop = 2f;
            _recordingList.style.paddingBottom = 2f;
            _recordingList.style.borderTopWidth = 1f;
            _recordingList.style.borderBottomWidth = 1f;
            _recordingList.style.borderLeftWidth = 1f;
            _recordingList.style.borderRightWidth = 1f;
            var borderColor = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            _recordingList.style.borderTopColor = borderColor;
            _recordingList.style.borderBottomColor = borderColor;
            _recordingList.style.borderLeftColor = borderColor;
            _recordingList.style.borderRightColor = borderColor;
            _recordingList.RegisterCallback<DragUpdatedEvent>(OnRecordingDragUpdated);
            _recordingList.RegisterCallback<DragPerformEvent>(OnRecordingDragPerform);
            section.Add(_recordingList);

            _compositionBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { name = CompositionBoxName };
            _compositionBox.style.display = DisplayStyle.None;
            _compositionBox.style.marginTop = 4f;
            section.Add(_compositionBox);
            RebuildRecordingList();
            return section;
        }

        private static Button CreateSmallButton(string text, Action onClick, string tooltip)
        {
            var button = new Button(onClick) { text = text, tooltip = tooltip };
            button.style.marginLeft = 2f;
            return button;
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
        /// 入力欄を設定して REC を読み込む（テストとプログラムからの利用向け）。<paramref name="recordingPath"/> が空なら REC を並べない。
        /// <paramref name="profile"/> が null ならキャラの Character SO を自動で入れる（キャラを変えると差し替わる）。
        /// <paramref name="options"/> を省略すると既定（レイヤー単位・Timeline のフレームレート）にする（前回 UI で選んだ値は使わない）。
        /// </summary>
        internal void SetInputs(
            string recordingPath,
            FacialController controller,
            FacialCharacterProfileSO profile,
            string outputFolder,
            bool createTimeline = false,
            FacialAnimationClipExportOptions options = default)
        {
            SetInputs(
                string.IsNullOrWhiteSpace(recordingPath) ? Array.Empty<string>() : new[] { recordingPath },
                controller,
                profile,
                outputFolder,
                createTimeline,
                options);
        }

        /// <summary>複数の REC を並べて入力欄を設定する（テストとプログラムからの利用向け。並べた順に書き出し、先頭が対応表の基準）。</summary>
        internal void SetInputs(
            IReadOnlyList<string> recordingPaths,
            FacialController controller,
            FacialCharacterProfileSO profile,
            string outputFolder,
            bool createTimeline = false,
            FacialAnimationClipExportOptions options = default)
        {
            EnsureBuilt();
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
            SetRecordings(recordingPaths);
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

        /// <summary>REC の一覧を置き換えて読み込む（同じパスの重複は 1 本にまとめる）。</summary>
        internal void SetRecordings(IReadOnlyList<string> recordingPaths)
        {
            EnsureBuilt();
            _recordings.Clear();
            AddRecordingsCore(recordingPaths);
            OnRecordingsChanged("REC ファイルを追加してください。");
        }

        /// <summary>REC を一覧の末尾に追加して読み込む（すでに並んでいるパスは追加しない）。追加した本数を返す。</summary>
        internal int AddRecordings(IReadOnlyList<string> recordingPaths)
        {
            EnsureBuilt();
            int added = AddRecordingsCore(recordingPaths);
            OnRecordingsChanged(added == 0 ? "追加できる REC がありませんでした（同じファイルはすでに並んでいます）。" : null);
            return added;
        }

        /// <summary>フォルダ直下の REC（.fcrec）を名前順に一覧へ追加する（サブフォルダは見ない）。追加した本数を返す。</summary>
        internal int AddRecordingFolder(string folder)
        {
            EnsureBuilt();
            string[] files = FindRecordingsInFolder(folder);
            if (files.Length == 0)
            {
                SetStatus($"フォルダ '{folder}' に REC（{RecordingExtension}）がありません。", HelpBoxMessageType.Warning);
                return 0;
            }

            return AddRecordings(files);
        }

        /// <summary>一覧の <paramref name="index"/> 番目の REC を外す。</summary>
        internal void RemoveRecording(int index)
        {
            EnsureBuilt();
            if (index < 0 || index >= _recordings.Count)
            {
                return;
            }

            _recordings.RemoveAt(index);
            OnRecordingsChanged("REC ファイルを追加してください。");
        }

        /// <summary>並べた REC をすべて読み直し、対応表を作り直す。</summary>
        internal void Reload()
        {
            EnsureBuilt();
            for (int i = 0; i < _recordings.Count; i++)
            {
                _recordings[i] = Load(_recordings[i].Path);
            }

            OnRecordingsChanged("REC ファイルを追加してください。");
        }

        /// <summary>書き出す（テストとプログラムからの利用向け）。並べた REC を順に書き出し、すべて成功したときだけ true。</summary>
        internal bool Export()
        {
            EnsureBuilt();
            LastResult = null;
            LastResults = Array.Empty<RecAnimationClipBatchItem>();
            if (_recordings.Count == 0)
            {
                SetStatus("REC ファイルを追加してください。", HelpBoxMessageType.Info);
                return false;
            }

            // 対応表は読み込んだ先頭の REC に対して作ったもの。ファイルの中身が読み込み時と違えば読み直す。
            // 読み直しで選んでいた対応先が消えたときは、前の REC 用の選択を当てずに止めて選び直してもらう。
            bool hadChoices = _remapPanel.RecordingKey != null && !_remapPanel.BuildSourceRemap().IsIdentity;
            bool reloaded = false;
            for (int i = 0; i < _recordings.Count; i++)
            {
                if (_recordings[i].ReadResult == null || !IsCurrent(_recordings[i]))
                {
                    _recordings[i] = Load(_recordings[i].Path);
                    reloaded = true;
                }
            }

            if (reloaded)
            {
                OnRecordingsChanged(null);
            }

            string unreadable = DescribeUnreadable();
            if (unreadable != null)
            {
                SetStatus("読み込めない REC があるため書き出せません（詳細は Console）。外すか直してください:\n" + unreadable, HelpBoxMessageType.Error);
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

            if (_remapPanel.HasErrors)
            {
                SetStatus("入力源・レイヤーの対応表に誤りがあるため書き出せません。Source / Layer Remap を確認してください。", HelpBoxMessageType.Error);
                return false;
            }

            RecSourceRemap remap = _remapPanel.BuildSourceRemap();
            var items = new List<RecAnimationClipBatchItem>(_recordings.Count);
            bool showProgress = _recordings.Count > 1;
            try
            {
                for (int i = 0; i < _recordings.Count; i++)
                {
                    LoadedRecording recording = _recordings[i];
                    if (showProgress)
                    {
                        EditorUtility.DisplayProgressBar(
                            "Export AnimationClips",
                            $"{Path.GetFileName(recording.Path)} ({i + 1}/{_recordings.Count})",
                            (float)i / _recordings.Count);
                    }

                    items.Add(ExportOne(recording, remap));
                }
            }
            finally
            {
                if (showProgress)
                {
                    EditorUtility.ClearProgressBar();
                }
            }

            LastResults = items;
            bool allSucceeded = true;
            for (int i = 0; i < items.Count; i++)
            {
                allSucceeded &= items[i].Succeeded;
            }

            LastResult = allSucceeded ? items[0].Result : null;
            ShowExportSummary(items);
            return allSucceeded;
        }

        private RecAnimationClipBatchItem ExportOne(LoadedRecording recording, RecSourceRemap remap)
        {
            try
            {
                bool success = RecAnimationClipExporter.TryExport(
                    recording.ReadResult,
                    recording.Path,
                    _controllerField.value as FacialController,
                    ProfileValue,
                    remap.IsIdentity ? null : remap,
                    _outputFolderField.value,
                    _createTimelineToggle.value,
                    out RecAnimationClipExportResult result,
                    _optionsFields.Options);
                return success
                    ? new RecAnimationClipBatchItem(recording.Path, result, null)
                    : new RecAnimationClipBatchItem(recording.Path, null, result.Error);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                return new RecAnimationClipBatchItem(
                    recording.Path, null, $"書き出し中にエラーが発生しました（保存したアセットは削除しました）: {ex.Message}");
            }
        }

        private void ShowExportSummary(IReadOnlyList<RecAnimationClipBatchItem> items)
        {
            if (items.Count == 1)
            {
                RecAnimationClipBatchItem only = items[0];
                if (!only.Succeeded)
                {
                    SetStatus(only.Error, HelpBoxMessageType.Error);
                    return;
                }

                var single = new StringBuilder();
                single.Append($"{only.Result.Clips.Count} 本の AnimationClip を書き出しました。");
                AppendResultDetails(single, only.Result, string.Empty);
                SetStatus(single.ToString(), only.Result.RemapProblems.Count > 0 ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info);
                return;
            }

            int succeeded = 0;
            int clipCount = 0;
            bool hasProblems = false;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Succeeded)
                {
                    succeeded++;
                    clipCount += items[i].Result.Clips.Count;
                    hasProblems |= items[i].Result.RemapProblems.Count > 0;
                }
            }

            var message = new StringBuilder();
            message.Append($"{items.Count} 本中 {succeeded} 本の REC から、計 {clipCount} 本の AnimationClip を書き出しました。");
            if (succeeded < items.Count)
            {
                message.Append($"\n{items.Count - succeeded} 本は書き出せませんでした（他の REC は書き出し済み）。");
            }

            for (int i = 0; i < items.Count; i++)
            {
                RecAnimationClipBatchItem item = items[i];
                message.Append("\n\n").Append(Path.GetFileName(item.RecordingPath)).Append(": ");
                if (!item.Succeeded)
                {
                    message.Append("失敗 — ").Append(item.Error);
                    continue;
                }

                message.Append(item.Result.Clips.Count).Append(" 本");
                AppendResultDetails(message, item.Result, "  ");
            }

            HelpBoxMessageType type = succeeded < items.Count
                ? HelpBoxMessageType.Error
                : hasProblems ? HelpBoxMessageType.Warning : HelpBoxMessageType.Info;
            SetStatus(message.ToString(), type);
        }

        private static void AppendResultDetails(StringBuilder message, RecAnimationClipExportResult result, string indent)
        {
            for (int i = 0; i < result.Clips.Count; i++)
            {
                message.Append('\n').Append(indent).Append(result.Clips[i].AssetPath);
            }

            if (result.TimelineAssetPath != null)
            {
                message.Append('\n').Append(indent).Append(result.TimelineAssetPath).Append("（AnimationTrack に Animator を Bind して再生）");
            }

            if (result.RemapProblems.Count > 0)
            {
                message.Append('\n').Append(indent).Append("未対応（再生されない）: ").Append(result.RemapProblems.Count).Append(" 件。Console を確認してください。");
            }
        }

        private int AddRecordingsCore(IReadOnlyList<string> recordingPaths)
        {
            if (recordingPaths == null)
            {
                return 0;
            }

            int added = 0;
            for (int i = 0; i < recordingPaths.Count; i++)
            {
                string path = recordingPaths[i];
                if (string.IsNullOrWhiteSpace(path) || ContainsRecording(path))
                {
                    continue;
                }

                _recordings.Add(Load(path));
                added++;
            }

            return added;
        }

        private bool ContainsRecording(string path)
        {
            string fullPath = ToComparablePath(path);
            for (int i = 0; i < _recordings.Count; i++)
            {
                if (string.Equals(ToComparablePath(_recordings[i].Path), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string ToComparablePath(string path)
        {
            try
            {
                return Path.GetFullPath(path).Replace('\\', '/');
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is System.Security.SecurityException)
            {
                return path;
            }
        }

        internal static string[] FindRecordingsInFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(folder, "*" + RecordingExtension, SearchOption.TopDirectoryOnly);
            var matched = new List<string>(files.Length);
            for (int i = 0; i < files.Length; i++)
            {
                // "*.fcrec" は Windows で ".fcrec~" 等にも当たるので拡張子で絞る。
                if (string.Equals(Path.GetExtension(files[i]), RecordingExtension, StringComparison.OrdinalIgnoreCase))
                {
                    matched.Add(files[i].Replace('\\', '/'));
                }
            }

            matched.Sort(StringComparer.OrdinalIgnoreCase);
            return matched.ToArray();
        }

        private LoadedRecording Load(string path)
        {
            RecordingLoadCount++;
            if (!RecFileReader.TryRead(path, out RecBinaryFormat.ReadResult readResult, out byte[] recordingBytes))
            {
                return LoadedRecording.Unreadable(path);
            }

            ReadFileStamp(path, out long length, out DateTime writeTimeUtc);
            return new LoadedRecording(
                path,
                readResult,
                RecTimelineExportWindow.HashRecording(recordingBytes),
                length,
                writeTimeUtc,
                readResult.Timeline != null ? RecSourceRemapResolver.CollectRecording(readResult.Timeline) : null);
        }

        /// <summary>一覧が変わったあとに、一覧の表示・対応表・構成の警告・ステータスを更新する。</summary>
        /// <param name="emptyMessage">一覧が空のときに出す文言（null ならステータスを変えない）。</param>
        private void OnRecordingsChanged(string emptyMessage)
        {
            RebuildRecordingList();
            RebuildRemap();
            UpdateCompositionWarning();
            if (_recordings.Count == 0)
            {
                if (emptyMessage != null)
                {
                    SetStatus(emptyMessage, HelpBoxMessageType.Info);
                }

                return;
            }

            string unreadable = DescribeUnreadable();
            if (unreadable != null)
            {
                SetStatus("REC を読み込めませんでした（詳細は Console）:\n" + unreadable, HelpBoxMessageType.Error);
                return;
            }

            if (emptyMessage != null)
            {
                string loaded = _recordings.Count == 1 ? "REC を読み込みました。" : $"{_recordings.Count} 本の REC を読み込みました。";
                SetStatus(ProfileValue == null ? "Profile（またはキャラ）を選んでください。" : loaded, HelpBoxMessageType.Info);
            }
        }

        private string DescribeUnreadable()
        {
            StringBuilder message = null;
            for (int i = 0; i < _recordings.Count; i++)
            {
                if (_recordings[i].ReadResult != null)
                {
                    continue;
                }

                message = message == null ? new StringBuilder() : message.Append('\n');
                message.Append(_recordings[i].Path);
            }

            return message?.ToString();
        }

        private void RebuildRecordingList()
        {
            _recordingList.Clear();
            if (_recordings.Count == 0)
            {
                var empty = new Label("REC がありません。Add File / Add Folder で追加するか、.fcrec かフォルダをここへドロップしてください。");
                empty.style.whiteSpace = WhiteSpace.Normal;
                empty.style.color = new Color(0.6f, 0.6f, 0.6f);
                _recordingList.Add(empty);
                return;
            }

            for (int i = 0; i < _recordings.Count; i++)
            {
                int index = i;
                LoadedRecording recording = _recordings[i];
                var row = new VisualElement { tooltip = recording.Path };
                row.style.flexDirection = FlexDirection.Row;
                row.style.alignItems = Align.Center;

                string caption = Path.GetFileName(recording.Path);
                if (i == 0 && _recordings.Count > 1)
                {
                    caption += "（対応表の基準）";
                }

                if (recording.ReadResult == null)
                {
                    caption += " — 読み込めません";
                }

                var label = new Label(caption);
                label.style.flexGrow = 1f;
                label.style.flexShrink = 1f;
                label.style.overflow = Overflow.Hidden;
                label.style.textOverflow = TextOverflow.Ellipsis;
                row.Add(label);

                var remove = new Button(() => RemoveRecording(index)) { text = "×", tooltip = "一覧から外す" };
                remove.style.width = 22f;
                row.Add(remove);
                _recordingList.Add(row);
            }
        }

        /// <summary>先頭の REC と入力源・レイヤーの構成が違う REC を警告する（対応表は先頭の REC にしか合わせていないため）。</summary>
        private void UpdateCompositionWarning()
        {
            RecRemapRecording reference = _recordings.Count > 0 ? _recordings[0].Composition : null;
            StringBuilder message = null;
            if (reference != null)
            {
                for (int i = 1; i < _recordings.Count; i++)
                {
                    string difference = DescribeCompositionDifference(reference, _recordings[i].Composition);
                    if (difference == null)
                    {
                        continue;
                    }

                    message ??= new StringBuilder(
                        $"入力源・レイヤーの構成が先頭の REC（{Path.GetFileName(_recordings[0].Path)}）と違う REC があります。" +
                        "Source / Layer Remap は先頭の REC に合わせて作るため、先頭に無い入力源・レイヤーは付け替えずに記録どおりの id で書き出します。");
                    message.Append("\n").Append(Path.GetFileName(_recordings[i].Path)).Append(": ").Append(difference);
                }
            }

            bool show = message != null;
            _compositionBox.text = show ? message.ToString() : string.Empty;
            _compositionBox.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>
        /// <paramref name="other"/> の入力源 id・レイヤー名の集合が <paramref name="reference"/> と違えば差分の説明を返す（同じなら null。読めない REC は比べない）。
        /// </summary>
        internal static string DescribeCompositionDifference(RecRemapRecording reference, RecRemapRecording other)
        {
            if (reference == null || other == null)
            {
                return null;
            }

            var referenceSources = new List<string>(reference.Sources.Count);
            for (int i = 0; i < reference.Sources.Count; i++)
            {
                referenceSources.Add(reference.Sources[i].Id);
            }

            var otherSources = new List<string>(other.Sources.Count);
            for (int i = 0; i < other.Sources.Count; i++)
            {
                otherSources.Add(other.Sources[i].Id);
            }

            var parts = new List<string>();
            AppendSetDifference(parts, "入力源", referenceSources, otherSources);
            AppendSetDifference(parts, "レイヤー", reference.LayerNames, other.LayerNames);
            return parts.Count == 0 ? null : string.Join(" / ", parts);
        }

        private static void AppendSetDifference(List<string> parts, string kind, IReadOnlyList<string> reference, IReadOnlyList<string> other)
        {
            var referenceSet = new HashSet<string>(reference, StringComparer.Ordinal);
            var otherSet = new HashSet<string>(other, StringComparer.Ordinal);
            var added = new List<string>();
            for (int i = 0; i < other.Count; i++)
            {
                if (!referenceSet.Contains(other[i]))
                {
                    added.Add(other[i]);
                }
            }

            var missing = new List<string>();
            for (int i = 0; i < reference.Count; i++)
            {
                if (!otherSet.Contains(reference[i]))
                {
                    missing.Add(reference[i]);
                }
            }

            if (added.Count > 0)
            {
                parts.Add($"先頭に無い{kind} {string.Join(", ", added)}");
            }

            if (missing.Count > 0)
            {
                parts.Add($"先頭にあって無い{kind} {string.Join(", ", missing)}");
            }
        }

        /// <summary>
        /// ファイルが読み込み時と同じかを返す。ファイルのサイズ・更新時刻が同じなら読み直さず、
        /// 違うときだけ中身のハッシュを取り直して比べる。
        /// </summary>
        private static bool IsCurrent(LoadedRecording recording)
        {
            if (ReadFileStamp(recording.Path, out long length, out DateTime writeTimeUtc)
                && length == recording.Length
                && writeTimeUtc == recording.WriteTimeUtc)
            {
                return true;
            }

            return string.Equals(RecTimelineExportWindow.ComputeRecordingKey(recording.Path), recording.Key, StringComparison.Ordinal);
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

        private void OnRecordingDragUpdated(DragUpdatedEvent evt)
        {
            if (CollectDroppedRecordings().Count > 0)
            {
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy;
            }
        }

        private void OnRecordingDragPerform(DragPerformEvent evt)
        {
            List<string> dropped = CollectDroppedRecordings();
            if (dropped.Count == 0)
            {
                return;
            }

            DragAndDrop.AcceptDrag();
            AddRecordings(dropped);
        }

        /// <summary>ドラッグ中のパスから REC を集める（フォルダは直下の REC に展開する）。</summary>
        private static List<string> CollectDroppedRecordings()
        {
            var result = new List<string>();
            string[] paths = DragAndDrop.paths;
            if (paths == null)
            {
                return result;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (Directory.Exists(path))
                {
                    result.AddRange(FindRecordingsInFolder(path));
                }
                else if (string.Equals(Path.GetExtension(path), RecordingExtension, StringComparison.OrdinalIgnoreCase))
                {
                    result.Add(path.Replace('\\', '/'));
                }
            }

            return result;
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

        /// <summary>保持している先頭の REC と現在の Profile で対応表を作り直す（REC は読み直さない）。</summary>
        private void RebuildRemap()
        {
            FacialCharacterProfileSO profileAsset = ProfileValue;
            LoadedRecording reference = _recordings.Count > 0 && _recordings[0].ReadResult != null ? _recordings[0] : null;
            _remapPanel.Rebuild(
                reference?.Key,
                reference?.ReadResult.Timeline,
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

        /// <summary>最後に並べた REC のフォルダ（無ければ空）。ファイル・フォルダ選択の初期位置に使う。</summary>
        private string LastRecordingDirectory()
        {
            if (_recordings.Count == 0)
            {
                return string.Empty;
            }

            return Path.GetDirectoryName(_recordings[_recordings.Count - 1].Path) ?? string.Empty;
        }

        private void BrowseRecording()
        {
            string selected = EditorUtility.OpenFilePanel("Select REC File", LastRecordingDirectory(), RecordingExtension.TrimStart('.'));
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            AddRecordings(new[] { selected });
        }

        private void BrowseRecordingFolder()
        {
            string selected = EditorUtility.OpenFolderPanel("Select REC Folder", LastRecordingDirectory(), string.Empty);
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            AddRecordingFolder(selected);
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

        /// <summary>一覧に並べた REC 1 本（読み込んだ内容と、変化を見るための識別子）。</summary>
        private sealed class LoadedRecording
        {
            public LoadedRecording(
                string path,
                RecBinaryFormat.ReadResult readResult,
                string key,
                long length,
                DateTime writeTimeUtc,
                RecRemapRecording composition)
            {
                Path = path;
                ReadResult = readResult;
                Key = key;
                Length = length;
                WriteTimeUtc = writeTimeUtc;
                Composition = composition;
            }

            public string Path { get; }

            /// <summary>読み込んだ内容（読み込めなければ null）。</summary>
            public RecBinaryFormat.ReadResult ReadResult { get; }

            /// <summary>中身のハッシュ（<see cref="RecTimelineExportWindow.HashRecording"/>）。</summary>
            public string Key { get; }

            public long Length { get; }

            public DateTime WriteTimeUtc { get; }

            /// <summary>入力源・レイヤーの構成（先頭の REC との比較に使う）。</summary>
            public RecRemapRecording Composition { get; }

            public static LoadedRecording Unreadable(string path)
            {
                return new LoadedRecording(path, null, null, -1, default, null);
            }
        }
    }

    /// <summary><see cref="RecAnimationClipExportWindow"/> でまとめて書き出したときの REC 1 本分の結果。</summary>
    internal sealed class RecAnimationClipBatchItem
    {
        public RecAnimationClipBatchItem(string recordingPath, RecAnimationClipExportResult result, string error)
        {
            RecordingPath = recordingPath;
            Result = result;
            Error = error;
        }

        public string RecordingPath { get; }

        /// <summary>書き出し結果（失敗・例外のときは null）。</summary>
        public RecAnimationClipExportResult Result { get; }

        /// <summary>書き出さなかった理由（成功なら null）。</summary>
        public string Error { get; }

        public bool Succeeded => Result != null;
    }
}
