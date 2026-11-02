using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.Timeline;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// REC（.fcrec）を TimelineAsset に書き出すウィンドウ。
    /// </summary>
    /// <remarks>
    /// <para>入力は REC ファイル / Profile / 出力 TimelineAsset（任意）だけ。Director と Facial Character トラックの binding は
    /// ユーザーが Timeline で設定するため指定欄を持たない（Req 10.5 / HID-408）。</para>
    /// <para>チャネル種別（Analog / Gaze / ValueProvider）は <see cref="RecToTimelineExporter.DetectChannels(RecBinaryFormat.ReadResult, FacialCharacterProfileSO)"/>
    /// の自動判定に任せ、上書き欄（Source Overrides）は持たない。REC 読み込み後に検出結果（source id / 判定結果 / 理由）を
    /// 読み取り専用で表示する。トリガー専用の source は検出結果に含まれないため表示しない（Req 10.1 / 10.2）。</para>
    /// </remarks>
    public sealed class RecTimelineExportWindow : EditorWindow
    {
        internal const string DetectionListName = "rec-export-detections";

        internal const string NextStepsMessage =
            "次の手順: 1) PlayableDirector に TimelineAsset をセット 2) Facial Character トラックの binding に FacialController をセット";

        internal const string RemapListName = "rec-export-remap";
        internal const string RemapProblemBoxName = "rec-export-remap-problems";

        /// <summary>対応表のドロップダウンで「REC と同じ id / 名前のまま」を表す選択肢。</summary>
        internal const string KeepChoice = "(そのまま)";

        /// <summary>対応表のドロップダウンで「書き出さない」を表す選択肢。</summary>
        internal const string ExcludeChoice = "(除外)";

        private readonly List<string> _listedSourceIds = new List<string>();
        private readonly Dictionary<string, string> _sourceChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _layerChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _sourceStatusLabels = new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _layerStatusLabels = new Dictionary<string, Label>(StringComparer.Ordinal);

        private TextField _recordingPathField;
        private ObjectField _profileField;
        private ObjectField _timelineField;
        private ScrollView _detectionList;
        private ScrollView _remapList;
        private HelpBox _remapProblemBox;
        private HelpBox _statusBox;
        private RecTimeline _remapTimeline;
        private FacialProfile? _remapProfile;
        private RecRemapReport _remapReport;
        private bool _built;

        [MenuItem("Tools/FacialControl/Timeline/REC Export")]
        public static void Open()
        {
            RecTimelineExportWindow window = GetWindow<RecTimelineExportWindow>();
            window.titleContent = new GUIContent("REC Export");
            window.minSize = new Vector2(520f, 420f);
        }

        /// <summary>テスト用: 検出結果リストに表示している source id。</summary>
        internal IReadOnlyList<string> ListedSourceIds => _listedSourceIds;

        /// <summary>テスト用: ステータス欄の文言。</summary>
        internal string StatusText => _statusBox != null ? _statusBox.text : string.Empty;

        /// <summary>テスト用: 対応表を当てた現在の判定結果（REC と Profile が揃うまで null）。</summary>
        internal RecRemapReport RemapReport => _remapReport;

        /// <summary>テスト用: 未対応・衝突の一覧の文言（無ければ空）。</summary>
        internal string RemapProblemText =>
            _remapProblemBox != null && _remapProblemBox.style.display.value != DisplayStyle.None ?_remapProblemBox.text : string.Empty;

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

            var browseRecordingButton = new Button(BrowseRecording)
            {
                text = "Browse REC"
            };
            recordingButtons.Add(browseRecordingButton);

            var reloadSourcesButton = new Button(RefreshDetections)
            {
                text = "Reload Sources"
            };
            reloadSourcesButton.style.marginLeft = 6f;
            recordingButtons.Add(reloadSourcesButton);

            _profileField = new ObjectField("Profile")
            {
                objectType = typeof(FacialCharacterProfileSO),
                allowSceneObjects = false,
            };
            // Gaze 判定は Profile（GazeChannels / binding の gaze 宣言）に依存するため、変更したら検出し直す。
            _profileField.RegisterValueChangedCallback(_ => RefreshDetectionsIfRecordingSelected());
            root.Add(_profileField);

            _timelineField = new ObjectField("Output Timeline")
            {
                objectType = typeof(TimelineAsset),
                allowSceneObjects = false,
            };
            root.Add(_timelineField);

            var detectionHeader = new Label("Detected Channels（自動判定）");
            detectionHeader.style.marginTop = 8f;
            detectionHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(detectionHeader);

            _detectionList = new ScrollView(ScrollViewMode.Vertical) { name = DetectionListName };
            _detectionList.style.flexGrow = 1f;
            _detectionList.style.minHeight = 180f;
            _detectionList.style.marginTop = 4f;
            root.Add(_detectionList);

            var remapHeader = new Label("Source / Layer Remap（REC の入力源・レイヤー → Profile）");
            remapHeader.style.marginTop = 8f;
            remapHeader.style.unityFontStyleAndWeight = FontStyle.Bold;
            root.Add(remapHeader);

            _remapList = new ScrollView(ScrollViewMode.Vertical) { name = RemapListName };
            _remapList.style.minHeight = 80f;
            _remapList.style.marginTop = 4f;
            root.Add(_remapList);

            _remapProblemBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { name = RemapProblemBoxName };
            _remapProblemBox.style.display = DisplayStyle.None;
            root.Add(_remapProblemBox);

            _statusBox = new HelpBox("Select a REC file and reload sources.", HelpBoxMessageType.Info);
            _statusBox.style.marginTop = 8f;
            root.Add(_statusBox);

            var exportButton = new Button(Export)
            {
                text = "Export Timeline"
            };
            exportButton.style.marginTop = 8f;
            root.Add(exportButton);
        }

        /// <summary>入力欄を設定する（テストとプログラムからの利用向け）。</summary>
        internal void SetInputs(string recordingPath, FacialCharacterProfileSO profile)
        {
            EnsureBuilt();
            _recordingPathField.value = recordingPath ?? string.Empty;
            _profileField.SetValueWithoutNotify(profile);
        }

        private void BrowseRecording()
        {
            string initialDirectory = string.Empty;
            string currentPath = _recordingPathField.value;
            if (!string.IsNullOrWhiteSpace(currentPath))
            {
                initialDirectory = System.IO.Path.GetDirectoryName(currentPath);
            }

            string selected = EditorUtility.OpenFilePanel("Select REC File", initialDirectory ?? string.Empty, "fcrec");
            if (string.IsNullOrWhiteSpace(selected))
            {
                return;
            }

            _recordingPathField.value = selected;
            RefreshDetections();
        }

        private void RefreshDetectionsIfRecordingSelected()
        {
            if (_recordingPathField != null && !string.IsNullOrWhiteSpace(_recordingPathField.value))
            {
                RefreshDetections();
            }
        }

        /// <summary>REC を読み込み、チャネル検出結果を読み取り専用リストに表示する。</summary>
        internal void RefreshDetections()
        {
            EnsureBuilt();
            _listedSourceIds.Clear();
            _detectionList.Clear();

            if (!RecFileReader.TryRead(_recordingPathField.value, out RecBinaryFormat.ReadResult readResult))
            {
                RebuildRemap(null, null);
                SetStatus("REC load failed. Check Console for details.", HelpBoxMessageType.Error);
                return;
            }

            var profile = _profileField.value as FacialCharacterProfileSO;
            IReadOnlyList<ChannelDetection> detections = RecToTimelineExporter.DetectChannels(readResult, profile);
            for (int i = 0; i < detections.Count; i++)
            {
                _listedSourceIds.Add(detections[i].SourceId);
                _detectionList.Add(CreateDetectionRow(detections[i]));
            }

            if (detections.Count == 0)
            {
                _detectionList.Add(new Label("値チャネル（Analog / Gaze / ValueProvider）はありません。"));
            }

            RebuildRemap(readResult.Timeline, profile);

            string message = $"Loaded {detections.Count} value channel(s) from REC.";
            if (profile == null)
            {
                message += " Profile を選ぶと Gaze 判定に Profile の GazeChannels と binding の gaze 宣言が使われます。";
            }

            SetStatus(message, HelpBoxMessageType.Info);
        }

        /// <summary>
        /// 対応表の欄を作り直す。行は値チャネルを記録した入力源と、レイヤー weight を記録したレイヤー。既定は「そのまま」（同じ id）。
        /// 同じ REC と Profile のまま作り直すときは、選んだ対応先を残す。
        /// </summary>
        private void RebuildRemap(RecTimeline timeline, FacialCharacterProfileSO profileAsset)
        {
            _remapList.Clear();
            _sourceStatusLabels.Clear();
            _layerStatusLabels.Clear();
            _remapTimeline = timeline;
            _remapProfile = profileAsset != null ? TimelineProfileSource.Resolve(profileAsset) : (FacialProfile?)null;
            _remapReport = null;
            if (timeline == null || !_remapProfile.HasValue)
            {
                _sourceChoices.Clear();
                _layerChoices.Clear();
                _remapList.Add(new Label(timeline == null
                    ? "REC を読み込むと対応表を編集できます。"
                    : "Profile を選ぶと、REC の入力源・レイヤーを Profile の入力源・レイヤーへ対応付けられます。"));
                UpdateRemapProblems();
                return;
            }

            FacialProfile profile = _remapProfile.Value;
            RecRemapReport initial = RecSourceRemapResolver.Analyze(timeline, profile, RecSourceRemap.Identity);
            var sourceTargets = new List<string> { KeepChoice };
            sourceTargets.AddRange(RecSourceRemapResolver.CollectTargetSourceIds(profile));
            sourceTargets.Add(ExcludeChoice);
            var layerTargets = new List<string> { KeepChoice };
            layerTargets.AddRange(RecSourceRemapResolver.CollectTargetLayerNames(profile));
            layerTargets.Add(ExcludeChoice);

            RetainOnly(_sourceChoices, initial.Sources, entry => entry.RecordedId, sourceTargets, allowCustomId: true);
            RetainOnly(_layerChoices, initial.Layers, entry => entry.RecordedName, layerTargets, allowCustomId: false);

            if (initial.Sources.Count == 0 && initial.Layers.Count == 0)
            {
                _remapList.Add(new Label("付け替えの対象（値チャネル・レイヤー weight）はありません。"));
            }

            for (int i = 0; i < initial.Sources.Count; i++)
            {
                string recordedId = initial.Sources[i].RecordedId;
                _remapList.Add(CreateRemapRow("Source", recordedId, sourceTargets, _sourceChoices, _sourceStatusLabels, allowCustomId: true));
            }

            for (int i = 0; i < initial.Layers.Count; i++)
            {
                string recordedName = initial.Layers[i].RecordedName;
                _remapList.Add(CreateRemapRow("Layer", recordedName, layerTargets, _layerChoices, _layerStatusLabels, allowCustomId: false));
            }

            UpdateRemapProblems();
        }

        private static void RetainOnly<T>(
            Dictionary<string, string> choices,
            IReadOnlyList<T> entries,
            Func<T, string> keyOf,
            List<string> validChoices,
            bool allowCustomId)
        {
            var keep = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                string key = keyOf(entries[i]);
                keep[key] = choices.TryGetValue(key, out string choice) && (allowCustomId || validChoices.Contains(choice))
                    ? choice
                    : KeepChoice;
            }

            choices.Clear();
            foreach (KeyValuePair<string, string> pair in keep)
            {
                choices.Add(pair.Key, pair.Value);
            }
        }

        private VisualElement CreateRemapRow(
            string kind,
            string recordedId,
            List<string> targets,
            Dictionary<string, string> choices,
            Dictionary<string, Label> statusLabels,
            bool allowCustomId)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;

            var label = new Label($"{kind}: {recordedId}");
            label.style.flexGrow = 1f;
            label.style.flexBasis = 0f;
            row.Add(label);

            string current = choices[recordedId];
            bool isCustom = !targets.Contains(current);
            var dropdown = new DropdownField(targets, isCustom ? KeepChoice : current);
            dropdown.style.width = 180f;
            row.Add(dropdown);

            // Analog / 目線の入力源は AdapterBinding が registry に直接登録するため、レイヤーの inputSources にある id だけでは
            // 対応先を選べない（例: iFacialMocap の ifm:head）。入力源は id を直接入力しても対応付けられるようにする。
            TextField customField = null;
            if (allowCustomId)
            {
                customField = new TextField { value = isCustom ? current : string.Empty, tooltip = "対応先の入力源 id を直接入力（空ならドロップダウンの選択）" };
                customField.style.width = 120f;
                customField.RegisterValueChangedCallback(evt =>
                {
                    string typed = evt.newValue != null ? evt.newValue.Trim() : string.Empty;
                    choices[recordedId] = typed.Length > 0 ? typed : dropdown.value;
                    UpdateRemapProblems();
                });
                row.Add(customField);
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                choices[recordedId] = evt.newValue;
                customField?.SetValueWithoutNotify(string.Empty);
                UpdateRemapProblems();
            });

            var status = new Label();
            status.style.width = 160f;
            status.style.marginLeft = 6f;
            status.style.whiteSpace = WhiteSpace.Normal;
            row.Add(status);
            statusLabels[recordedId] = status;
            return row;
        }

        /// <summary>入力欄の選択から対応表を作る（「そのまま」は対応表に入れない）。</summary>
        internal RecSourceRemap BuildSourceRemap()
        {
            return new RecSourceRemap(ToMap(_sourceChoices), ToMap(_layerChoices));
        }

        /// <summary>
        /// 対応先を選ぶ（テストとプログラムからの利用向け）。<paramref name="target"/> が null なら除外、
        /// <see cref="KeepChoice"/> ならそのまま。REC と Profile を読み込んだ後に呼ぶ。
        /// </summary>
        internal void SetRemapChoice(bool isLayer, string recordedId, string target)
        {
            Dictionary<string, string> choices = isLayer ? _layerChoices : _sourceChoices;
            if (!choices.ContainsKey(recordedId))
            {
                throw new ArgumentException($"'{recordedId}' is not a remappable {(isLayer ? "layer" : "source")}.", nameof(recordedId));
            }

            choices[recordedId] = target ?? ExcludeChoice;
            UpdateRemapProblems();
        }

        private static List<KeyValuePair<string, string>> ToMap(Dictionary<string, string> choices)
        {
            var map = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> pair in choices)
            {
                if (pair.Value == KeepChoice)
                {
                    continue;
                }

                map.Add(new KeyValuePair<string, string>(pair.Key, pair.Value == ExcludeChoice ? null : pair.Value));
            }

            return map;
        }

        private void UpdateRemapProblems()
        {
            if (_remapTimeline == null || !_remapProfile.HasValue)
            {
                _remapReport = null;
                _remapProblemBox.style.display = DisplayStyle.None;
                return;
            }

            _remapReport = RecSourceRemapResolver.Analyze(_remapTimeline, _remapProfile.Value, BuildSourceRemap());
            for (int i = 0; i < _remapReport.Sources.Count; i++)
            {
                RecRemapSourceEntry entry = _remapReport.Sources[i];
                if (_sourceStatusLabels.TryGetValue(entry.RecordedId, out Label label))
                {
                    label.text = StatusLabel(entry.Status, entry.TargetId, _remapProfile.Value);
                }
            }

            for (int i = 0; i < _remapReport.Layers.Count; i++)
            {
                RecRemapLayerEntry entry = _remapReport.Layers[i];
                if (_layerStatusLabels.TryGetValue(entry.RecordedName, out Label label))
                {
                    label.text = StatusLabel(entry.Status, null, _remapProfile.Value);
                }
            }

            IReadOnlyList<string> problems = RecSourceRemapResolver.DescribeProblems(_remapReport);
            if (problems.Count == 0)
            {
                _remapProblemBox.style.display = DisplayStyle.None;
                _remapProblemBox.text = string.Empty;
                return;
            }

            _remapProblemBox.messageType = _remapReport.HasCollisions ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
            _remapProblemBox.text = "未対応（Export 前に確認してください）:\n" + string.Join("\n", problems);
            _remapProblemBox.style.display = DisplayStyle.Flex;
        }

        /// <summary>対応表の行の状態表示。入力源は、対応先を宣言している Profile のレイヤーも示す。</summary>
        internal static string StatusLabel(RecRemapStatus status, string targetSourceId, FacialProfile profile)
        {
            switch (status)
            {
                case RecRemapStatus.Mapped:
                    if (targetSourceId == null)
                    {
                        return "OK";
                    }

                    IReadOnlyList<string> layers = RecSourceRemapResolver.FindDeclaringLayers(profile, targetSourceId);
                    return layers.Count > 0 ? $"OK（レイヤー {string.Join(", ", layers)}）" : "OK";
                case RecRemapStatus.NotInTarget:
                    return "未対応（Profile に無い）";
                case RecRemapStatus.Unchecked:
                    return "未確認（binding が登録していれば再生）";
                case RecRemapStatus.Excluded:
                    return "書き出さない";
                case RecRemapStatus.Collision:
                    return "対応先が重複";
                default:
                    return status.ToString();
            }
        }

        private static VisualElement CreateDetectionRow(ChannelDetection detection)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 4f;

            var sourceLabel = new Label(detection.SourceId);
            sourceLabel.style.flexGrow = 1f;
            sourceLabel.style.flexBasis = 0f;
            row.Add(sourceLabel);

            var kindLabel = new Label(KindLabel(detection));
            kindLabel.style.width = 200f;
            row.Add(kindLabel);

            var reasonLabel = new Label(ReasonLabel(detection.Reason));
            reasonLabel.style.flexGrow = 1f;
            reasonLabel.style.flexBasis = 0f;
            reasonLabel.style.whiteSpace = WhiteSpace.Normal;
            row.Add(reasonLabel);
            return row;
        }

        /// <summary>種別の表示文（例: <c>Analog（1 軸）</c> / <c>ValueProvider（52 個の BlendShape）</c>）。</summary>
        internal static string KindLabel(ChannelDetection detection)
        {
            return detection.Kind == FacialValueChannelKind.ValueProvider
                ? $"{detection.Kind}（{detection.AxisCount} 個の BlendShape）"
                : $"{detection.Kind}（{detection.AxisCount} 軸）";
        }

        /// <summary>判定理由の表示文。</summary>
        internal static string ReasonLabel(ChannelDetectionReason reason)
        {
            switch (reason)
            {
                case ChannelDetectionReason.ValueProviderNamed:
                    return "値提供型の記録（BlendShape を録画時の名前で保存）";
                case ChannelDetectionReason.ValueProviderIndexed:
                    return "値提供型の記録（REC に BlendShape 名が無いため index で保存。録り直すと名前で保存）";
                case ChannelDetectionReason.ExplicitGazeSourceId:
                    return "明示した Gaze の source id と一致";
                case ChannelDetectionReason.ConventionGazeChannel:
                    return "Gaze の規約 id（Profile の GazeChannels にあるチャネル）";
                case ChannelDetectionReason.GazeProviderDeclaration:
                    return "Profile の binding が gaze source を宣言";
                case ChannelDetectionReason.NonTwoAxisSamples:
                    return "Gaze 候補だが 2 軸でないため Analog";
                case ChannelDetectionReason.DefaultAnalog:
                    return "Gaze の手がかりなし（Analog）";
                case ChannelDetectionReason.Overridden:
                    return "上書き指定";
                default:
                    return reason.ToString();
            }
        }

        private void Export()
        {
            string outputPath = ResolveOutputPath();
            if (string.IsNullOrWhiteSpace(outputPath))
            {
                SetStatus("Export was cancelled before selecting an output TimelineAsset.", HelpBoxMessageType.Warning);
                return;
            }

            ExportTo(outputPath);
        }

        /// <summary>指定パスへ Export し、完了したら残りの手順をステータスに表示する。</summary>
        internal bool ExportTo(string outputPath)
        {
            EnsureBuilt();
            var profile = _profileField.value as FacialCharacterProfileSO;
            if (profile == null)
            {
                SetStatus("Profile is required.", HelpBoxMessageType.Error);
                return false;
            }

            if (_remapReport != null && _remapReport.HasCollisions)
            {
                SetStatus("対応表の対応先が重複しているため Export できません。Source / Layer Remap を確認してください。", HelpBoxMessageType.Error);
                return false;
            }

            // すべて「そのまま」なら付け替えなしの従来の Export と同じにする（未対応はこのウィンドウの一覧で示している）。
            RecSourceRemap remap = BuildSourceRemap();
            var existingTimeline = _timelineField.value as TimelineAsset;
            bool success = RecToTimelineExporter.TryExportTimelineAsset(
                _recordingPathField.value,
                profile,
                outputPath,
                out RecToTimelineExporter.ExportResult result,
                existingTimeline,
                sourceRemap: remap.IsIdentity ? null : remap);

            if (!success)
            {
                if (result != null && result.Cancelled)
                {
                    SetStatus("Export cancelled.", HelpBoxMessageType.Warning);
                }
                else
                {
                    SetStatus("Export failed. Check Console for details.", HelpBoxMessageType.Error);
                }

                return false;
            }

            _timelineField.SetValueWithoutNotify(result.Timeline);
            SetStatus($"Exported TimelineAsset to '{result.OutputAssetPath}'.\n{NextStepsMessage}", HelpBoxMessageType.Info);
            return true;
        }

        private string ResolveOutputPath()
        {
            if (_timelineField.value is TimelineAsset timelineAsset)
            {
                return AssetDatabase.GetAssetPath(timelineAsset);
            }

            return EditorUtility.SaveFilePanelInProject(
                "Export TimelineAsset",
                "RecordedTimeline",
                "playable",
                "Choose where to save the exported TimelineAsset.");
        }

        private void SetStatus(string message, HelpBoxMessageType messageType)
        {
            _statusBox.messageType = messageType;
            _statusBox.text = message;
        }
    }
}
