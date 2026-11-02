using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor.AnimationClipExport;
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

        private TextField _recordingPathField;
        private ObjectField _profileField;
        private ObjectField _timelineField;
        private ScrollView _detectionList;
        private HelpBox _statusBox;

        // 対応表の UI（REC から AnimationClip を書き出すウィンドウと共通。HID-409）。選択は、作った REC の識別子
        // （読み込んだバイト列の SHA-256）と Profile（instance ID。無しは 0）が同じ間だけ引き継ぐ。
        private RecSourceRemapPanel _remapPanel;
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
        internal RecRemapReport RemapReport => _remapPanel?.Report;

        /// <summary>テスト用: 未対応・衝突の一覧の文言（無ければ空）。</summary>
        internal string RemapProblemText => _remapPanel != null ? _remapPanel.ProblemText : string.Empty;

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

            _remapPanel = new RecSourceRemapPanel(RemapListName, RemapProblemBoxName);
            root.Add(_remapPanel);

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

            if (!RecFileReader.TryRead(_recordingPathField.value, out RecBinaryFormat.ReadResult readResult, out byte[] recordingBytes))
            {
                _remapPanel.Rebuild(null, null, null, 0);
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

            _remapPanel.Rebuild(
                HashRecording(recordingBytes),
                readResult.Timeline,
                profile != null ? TimelineProfileSource.Resolve(profile) : (FacialProfile?)null,
                profile != null ? profile.GetInstanceID() : 0);

            string message = $"Loaded {detections.Count} value channel(s) from REC.";
            if (profile == null)
            {
                message += " Profile を選ぶと Gaze 判定に Profile の GazeChannels と binding の gaze 宣言が使われます。";
            }

            SetStatus(message, HelpBoxMessageType.Info);
        }

        /// <summary>入力欄の選択から対応表を作る（「そのまま」は対応表に入れない）。</summary>
        internal RecSourceRemap BuildSourceRemap()
        {
            EnsureBuilt();
            return _remapPanel.BuildSourceRemap();
        }

        /// <summary>
        /// 対応先を選ぶ（テストとプログラムからの利用向け）。<paramref name="target"/> が null なら除外、
        /// <see cref="KeepChoice"/> ならそのまま、それ以外は id の直接入力と同じ。REC と Profile を読み込んだ後に呼ぶ。
        /// </summary>
        internal void SetRemapChoice(bool isLayer, string recordedId, string target)
        {
            EnsureBuilt();
            _remapPanel.SetRemapChoice(isLayer, recordedId, target);
        }

        /// <summary>REC の識別子（バイト列の SHA-256 の 16 進文字列）。null なら null。</summary>
        internal static string HashRecording(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            using (var sha = System.Security.Cryptography.SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty);
            }
        }

        /// <summary>REC 欄のファイルの現在の識別子。読めなければ null。</summary>
        internal static string ComputeRecordingKey(string recordingPath)
        {
            if (string.IsNullOrWhiteSpace(recordingPath))
            {
                return null;
            }

            try
            {
                return System.IO.File.Exists(recordingPath) ? HashRecording(System.IO.File.ReadAllBytes(recordingPath)) : null;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException || ex is NotSupportedException)
            {
                return null;
            }
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
                case RecRemapStatus.InvalidId:
                    return "id の形式が不正";
                case RecRemapStatus.MergedTriggers:
                    return "OK（表情トリガーがほかの入力源とまとまる）";
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

            // 対応表の選択は読み込んだ REC に対して作ったもの。REC 欄のファイルの中身が読み込み時と違えば当て直しを求める。
            // 読み込んでいない（識別子が null）ときも、選択があれば照合する（null と現在の識別子は一致しないので拒否される）。
            RecSourceRemap remap = BuildSourceRemap();
            if ((_remapPanel.RecordingKey != null || !remap.IsIdentity)
                && !string.Equals(ComputeRecordingKey(_recordingPathField.value), _remapPanel.RecordingKey, StringComparison.Ordinal))
            {
                SetStatus("REC が読み込み時から変わっています。Reload Sources で読み込み直し、対応表を確認してから Export してください。", HelpBoxMessageType.Error);
                return false;
            }

            if (_remapPanel.HasErrors)
            {
                SetStatus("対応表に重複した対応先か不正な id があるため Export できません。Source / Layer Remap を確認してください。", HelpBoxMessageType.Error);
                return false;
            }

            // すべて「そのまま」なら付け替えなしの従来の Export と同じにする（未対応はこのウィンドウの一覧で示している）。
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
