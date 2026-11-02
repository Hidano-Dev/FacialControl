using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Editor.AnimationClipExport
{
    /// <summary>
    /// REC の入力源・レイヤー → 対象 Profile の対応表を編集する UI 部品（HID-412 の対応表を HID-409 のウィンドウで使う）。
    /// </summary>
    /// <remarks>
    /// <para>判定（未対応・衝突・id の形式）は REC Export ウィンドウと同じ <see cref="RecSourceRemapResolver"/> に任せ、表示文言・選択肢も
    /// <see cref="RecTimelineExportWindow"/> と共有する。行は REC の入力源（値チャネル・表情トリガー）と、レイヤー weight を記録したレイヤー。</para>
    /// <para>REC 側・Profile 側の集計は <see cref="Rebuild"/> で 1 回だけ行い、選択の変更では対応表の判定だけをやり直す。
    /// 選択は同じ REC（recordingKey）と同じ Profile（profileKey）の間だけ残す。</para>
    /// </remarks>
    internal sealed class RecSourceRemapPanel : VisualElement
    {
        internal const string ProblemBoxName = "rec-remap-panel-problems";

        /// <summary>入力源 id を直接入力している行のドロップダウンの表示。</summary>
        internal const string CustomChoice = "(手入力)";

        private readonly Dictionary<string, string> _sourceChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _layerChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _sourceStatusLabels = new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _layerStatusLabels = new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly ScrollView _list;
        private readonly HelpBox _problemBox;
        private readonly HelpBox _noticeBox;
        private string _recordingKey;
        private int _profileKey;
        private RecRemapRecording _recording;
        private RecRemapTarget _target;
        private FacialProfile? _profile;

        public RecSourceRemapPanel()
            : this(null, ProblemBoxName)
        {
        }

        /// <param name="listName">行の一覧（ScrollView）の要素名。</param>
        /// <param name="problemBoxName">未対応・衝突の一覧（HelpBox）の要素名。</param>
        public RecSourceRemapPanel(string listName, string problemBoxName)
        {
            var header = new Label("Source / Layer Remap（REC の入力源・レイヤー → Profile）");
            header.style.marginTop = 8f;
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            Add(header);

            _noticeBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning);
            _noticeBox.style.display = DisplayStyle.None;
            Add(_noticeBox);

            _list = new ScrollView(ScrollViewMode.Vertical) { name = listName };
            _list.style.minHeight = 80f;
            _list.style.marginTop = 4f;
            Add(_list);

            _problemBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { name = problemBoxName };
            _problemBox.style.display = DisplayStyle.None;
            Add(_problemBox);
            Rebuild(null, null, null, 0);
        }

        /// <summary>テスト用: 対応表の上に出している注意書き（無ければ空）。</summary>
        public string NoticeText => _noticeBox.style.display.value != DisplayStyle.None ? _noticeBox.text : string.Empty;

        /// <summary>対応表の上に注意書き（例: キャラと Profile の食い違い）を出す。null / 空なら隠す。</summary>
        public void SetNotice(string message)
        {
            bool show = !string.IsNullOrEmpty(message);
            _noticeBox.text = show ? message : string.Empty;
            _noticeBox.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>対応表を当てた現在の判定結果（REC と Profile が揃うまで null）。</summary>
        public RecRemapReport Report { get; private set; }

        /// <summary>書き出しを止める問題（対応先の重複・形式が不正な入力源 id）があれば true。</summary>
        public bool HasErrors => Report != null && Report.HasErrors;

        /// <summary>この対応表を作った REC の識別子（<see cref="RecTimelineExportWindow.HashRecording"/>。読み込んでいなければ null）。</summary>
        public string RecordingKey => _recordingKey;

        /// <summary>未対応・衝突・除外の一覧の文言（無ければ空）。</summary>
        public string ProblemText => _problemBox.style.display.value != DisplayStyle.None ? _problemBox.text : string.Empty;

        /// <summary>
        /// 対応表の欄を作り直す。<paramref name="recordingKey"/> と <paramref name="profileKey"/> が前回と同じときだけ、選んだ対応先を残す。
        /// </summary>
        public void Rebuild(string recordingKey, RecTimeline timeline, FacialProfile? profile, int profileKey)
        {
            if (!string.Equals(recordingKey, _recordingKey, StringComparison.Ordinal) || profileKey != _profileKey)
            {
                _sourceChoices.Clear();
                _layerChoices.Clear();
            }

            _recordingKey = recordingKey;
            _profileKey = profileKey;
            _profile = profile;
            _recording = timeline != null ? RecSourceRemapResolver.CollectRecording(timeline) : null;
            _target = profile.HasValue ? RecSourceRemapResolver.CollectTarget(profile.Value) : null;
            _list.Clear();
            _sourceStatusLabels.Clear();
            _layerStatusLabels.Clear();
            if (_recording == null || _target == null)
            {
                // 見えない選択を持ち越さない。
                _sourceChoices.Clear();
                _layerChoices.Clear();
                _list.Add(new Label(timeline == null
                    ? "REC を読み込むと対応表を編集できます。"
                    : "Profile を選ぶと、REC の入力源・レイヤーを Profile の入力源・レイヤーへ対応付けられます。"));
                Refresh();
                return;
            }

            RecRemapReport initial = RecSourceRemapResolver.Analyze(_recording, _target, RecSourceRemap.Identity);
            var sourceTargets = new List<string> { RecTimelineExportWindow.KeepChoice };
            sourceTargets.AddRange(_target.SourceIds);
            sourceTargets.Add(RecTimelineExportWindow.ExcludeChoice);
            var layerTargets = new List<string> { RecTimelineExportWindow.KeepChoice };
            layerTargets.AddRange(_target.LayerNames);
            layerTargets.Add(RecTimelineExportWindow.ExcludeChoice);

            var sourceKeys = new List<string>();
            for (int i = 0; i < initial.Sources.Count; i++)
            {
                sourceKeys.Add(initial.Sources[i].RecordedId);
            }

            var layerKeys = new List<string>();
            for (int i = 0; i < initial.Layers.Count; i++)
            {
                layerKeys.Add(initial.Layers[i].RecordedName);
            }

            RetainOnly(_sourceChoices, sourceKeys, sourceTargets, allowCustomId: true);
            RetainOnly(_layerChoices, layerKeys, layerTargets, allowCustomId: false);
            if (sourceKeys.Count == 0 && layerKeys.Count == 0)
            {
                _list.Add(new Label("付け替えの対象（入力源・レイヤー weight）はありません。"));
            }

            for (int i = 0; i < initial.Sources.Count; i++)
            {
                RecRemapSourceEntry entry = initial.Sources[i];
                string caption = entry.TriggerCount > 0
                    ? $"Source: {entry.RecordedId}（表情トリガー {entry.TriggerCount} 件）"
                    : $"Source: {entry.RecordedId}";
                _list.Add(CreateRow(caption, entry.RecordedId, sourceTargets, _sourceChoices, _sourceStatusLabels, allowCustomId: true));
            }

            for (int i = 0; i < layerKeys.Count; i++)
            {
                _list.Add(CreateRow($"Layer: {layerKeys[i]}", layerKeys[i], layerTargets, _layerChoices, _layerStatusLabels, allowCustomId: false));
            }

            Refresh();
        }

        /// <summary>入力欄の選択から対応表を作る（「そのまま」は対応表に入れない）。</summary>
        public RecSourceRemap BuildSourceRemap()
        {
            return new RecSourceRemap(ToMap(_sourceChoices), ToMap(_layerChoices));
        }

        /// <summary>
        /// 対応先を選ぶ（テストとプログラムからの利用向け）。<paramref name="target"/> が null なら除外、
        /// <see cref="RecTimelineExportWindow.KeepChoice"/> ならそのまま、それ以外は id の直接入力と同じ。
        /// </summary>
        public void SetRemapChoice(bool isLayer, string recordedId, string target)
        {
            Dictionary<string, string> choices = isLayer ? _layerChoices : _sourceChoices;
            if (!choices.ContainsKey(recordedId))
            {
                throw new ArgumentException($"'{recordedId}' is not a remappable {(isLayer ? "layer" : "source")}.", nameof(recordedId));
            }

            choices[recordedId] = target ?? RecTimelineExportWindow.ExcludeChoice;
            Refresh();
        }

        private static void RetainOnly(
            Dictionary<string, string> choices,
            List<string> keys,
            List<string> validChoices,
            bool allowCustomId)
        {
            var keep = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++)
            {
                keep[keys[i]] = choices.TryGetValue(keys[i], out string choice) && (allowCustomId || validChoices.Contains(choice))
                    ? choice
                    : RecTimelineExportWindow.KeepChoice;
            }

            choices.Clear();
            foreach (KeyValuePair<string, string> pair in keep)
            {
                choices.Add(pair.Key, pair.Value);
            }
        }

        private VisualElement CreateRow(
            string caption,
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

            var label = new Label(caption);
            label.style.flexGrow = 1f;
            label.style.flexBasis = 0f;
            row.Add(label);

            string current = choices[recordedId];
            bool isCustom = allowCustomId && !targets.Contains(current);

            // 手入力の行は、ドロップダウンを「(手入力)」と表示する（「そのまま」に見えないようにし、選び直せるようにする）。
            var rowTargets = new List<string>(targets);
            if (allowCustomId)
            {
                rowTargets.Add(CustomChoice);
            }

            var dropdown = new DropdownField(rowTargets, isCustom ? CustomChoice : current);
            dropdown.style.width = 180f;
            row.Add(dropdown);

            // Analog / 目線の入力源は AdapterBinding が registry に直接登録するため、レイヤーの inputSources にある id だけでは
            // 対応先を選べない（例: iFacialMocap の ifm:head）。入力源は id を直接入力しても対応付けられるようにする。
            // 入力した id の形式は判定（RecRemapStatus.InvalidId）で確かめ、不正なら書き出しを止める。
            TextField customField = null;
            if (allowCustomId)
            {
                customField = new TextField { value = isCustom ? current : string.Empty, tooltip = "対応先の入力源 id を直接入力（空ならそのまま）" };
                customField.style.width = 120f;
                customField.RegisterValueChangedCallback(evt =>
                {
                    string typed = evt.newValue != null ? evt.newValue.Trim() : string.Empty;
                    if (typed.Length > 0)
                    {
                        choices[recordedId] = typed;
                        dropdown.SetValueWithoutNotify(CustomChoice);
                    }
                    else if (dropdown.value == CustomChoice)
                    {
                        choices[recordedId] = RecTimelineExportWindow.KeepChoice;
                        dropdown.SetValueWithoutNotify(RecTimelineExportWindow.KeepChoice);
                    }

                    Refresh();
                });
                row.Add(customField);
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                if (evt.newValue == CustomChoice)
                {
                    // 入力欄の id を使う（空なら「そのまま」と同じ）。
                    string typed = customField != null && customField.value != null ? customField.value.Trim() : string.Empty;
                    choices[recordedId] = typed.Length > 0 ? typed : RecTimelineExportWindow.KeepChoice;
                }
                else
                {
                    choices[recordedId] = evt.newValue;
                    customField?.SetValueWithoutNotify(string.Empty);
                }

                Refresh();
            });

            var status = new Label();
            status.style.width = 160f;
            status.style.marginLeft = 6f;
            status.style.whiteSpace = WhiteSpace.Normal;
            row.Add(status);
            statusLabels[recordedId] = status;
            return row;
        }

        private static List<KeyValuePair<string, string>> ToMap(Dictionary<string, string> choices)
        {
            var map = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> pair in choices)
            {
                if (pair.Value == RecTimelineExportWindow.KeepChoice)
                {
                    continue;
                }

                map.Add(new KeyValuePair<string, string>(
                    pair.Key, pair.Value == RecTimelineExportWindow.ExcludeChoice ? null : pair.Value));
            }

            return map;
        }

        /// <summary>現在の選択で対応表を判定し直す（REC は走査し直さない）。</summary>
        private void Refresh()
        {
            if (_recording == null || _target == null)
            {
                Report = null;
                _problemBox.style.display = DisplayStyle.None;
                _problemBox.text = string.Empty;
                return;
            }

            Report = RecSourceRemapResolver.Analyze(_recording, _target, BuildSourceRemap());
            for (int i = 0; i < Report.Sources.Count; i++)
            {
                RecRemapSourceEntry entry = Report.Sources[i];
                if (_sourceStatusLabels.TryGetValue(entry.RecordedId, out Label label))
                {
                    label.text = RecTimelineExportWindow.StatusLabel(entry.Status, entry.TargetId, _profile.Value);
                }
            }

            for (int i = 0; i < Report.Layers.Count; i++)
            {
                RecRemapLayerEntry entry = Report.Layers[i];
                if (_layerStatusLabels.TryGetValue(entry.RecordedName, out Label label))
                {
                    label.text = RecTimelineExportWindow.StatusLabel(entry.Status, null, _profile.Value);
                }
            }

            IReadOnlyList<string> problems = RecSourceRemapResolver.DescribeProblems(Report);
            if (problems.Count == 0)
            {
                _problemBox.style.display = DisplayStyle.None;
                _problemBox.text = string.Empty;
                return;
            }

            _problemBox.messageType = Report.HasErrors ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
            _problemBox.text = "未対応・除外（書き出し前に確認してください）:\n" + string.Join("\n", problems);
            _problemBox.style.display = DisplayStyle.Flex;
        }
    }
}
