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
    /// <para>判定（未対応・衝突）は REC Export ウィンドウと同じ <see cref="RecSourceRemapResolver"/> に任せ、表示文言・選択肢も
    /// <see cref="RecTimelineExportWindow"/> と共有する。行は値チャネルを記録した入力源と、レイヤー weight を記録したレイヤー。</para>
    /// <para>選択は同じ REC（<see cref="Rebuild"/> の recordingKey）の間だけ残し、REC が変わったら「そのまま」に戻す。
    /// 直接入力した入力源 id は <see cref="InputSourceId"/> として解釈できなければエラーにし、<see cref="HasErrors"/> を立てる。</para>
    /// </remarks>
    internal sealed class RecSourceRemapPanel : VisualElement
    {
        internal const string ProblemBoxName = "rec-remap-panel-problems";

        private readonly Dictionary<string, string> _sourceChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _layerChoices = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _sourceStatusLabels = new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _layerStatusLabels = new Dictionary<string, Label>(StringComparer.Ordinal);
        private readonly List<string> _invalidCustomIds = new List<string>();
        private readonly ScrollView _list;
        private readonly HelpBox _problemBox;
        private string _recordingKey;
        private RecTimeline _timeline;
        private FacialProfile? _profile;

        public RecSourceRemapPanel()
        {
            var header = new Label("Source / Layer Remap（REC の入力源・レイヤー → Profile）");
            header.style.marginTop = 8f;
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            Add(header);

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.minHeight = 80f;
            _list.style.marginTop = 4f;
            Add(_list);

            _problemBox = new HelpBox(string.Empty, HelpBoxMessageType.Warning) { name = ProblemBoxName };
            _problemBox.style.display = DisplayStyle.None;
            Add(_problemBox);
            Rebuild(null, null, null);
        }

        /// <summary>選択が変わったとき。</summary>
        public event Action Changed;

        /// <summary>対応表を当てた現在の判定結果（REC と Profile が揃うまで null）。</summary>
        public RecRemapReport Report { get; private set; }

        /// <summary>書き出しを止める問題（対応先の重複・解釈できない入力源 id）があれば true。</summary>
        public bool HasErrors => (Report != null && Report.HasCollisions) || _invalidCustomIds.Count > 0;

        /// <summary>未対応・衝突・入力 id の誤りの一覧の文言（無ければ空）。</summary>
        public string ProblemText => _problemBox.style.display.value != DisplayStyle.None ? _problemBox.text : string.Empty;

        /// <summary>
        /// 対応表の欄を作り直す。<paramref name="recordingKey"/>（REC のパス等）が前回と同じときだけ、選んだ対応先を残す。
        /// </summary>
        public void Rebuild(string recordingKey, RecTimeline timeline, FacialProfile? profile)
        {
            if (!string.Equals(recordingKey, _recordingKey, StringComparison.Ordinal))
            {
                _sourceChoices.Clear();
                _layerChoices.Clear();
            }

            _recordingKey = recordingKey;
            _timeline = timeline;
            _profile = profile;
            _list.Clear();
            _sourceStatusLabels.Clear();
            _layerStatusLabels.Clear();
            if (timeline == null || !profile.HasValue)
            {
                _sourceChoices.Clear();
                _layerChoices.Clear();
                _list.Add(new Label(timeline == null
                    ? "REC を読み込むと対応表を編集できます。"
                    : "Profile を選ぶと、REC の入力源・レイヤーを Profile の入力源・レイヤーへ対応付けられます。"));
                Refresh();
                return;
            }

            RecRemapReport initial = RecSourceRemapResolver.Analyze(timeline, profile.Value, RecSourceRemap.Identity);
            var sourceTargets = new List<string> { RecTimelineExportWindow.KeepChoice };
            sourceTargets.AddRange(RecSourceRemapResolver.CollectTargetSourceIds(profile.Value));
            sourceTargets.Add(RecTimelineExportWindow.ExcludeChoice);
            var layerTargets = new List<string> { RecTimelineExportWindow.KeepChoice };
            layerTargets.AddRange(RecSourceRemapResolver.CollectTargetLayerNames(profile.Value));
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
                _list.Add(new Label("付け替えの対象（値チャネル・レイヤー weight）はありません。"));
            }

            for (int i = 0; i < sourceKeys.Count; i++)
            {
                _list.Add(CreateRow("Source", sourceKeys[i], sourceTargets, _sourceChoices, _sourceStatusLabels, allowCustomId: true));
            }

            for (int i = 0; i < layerKeys.Count; i++)
            {
                _list.Add(CreateRow("Layer", layerKeys[i], layerTargets, _layerChoices, _layerStatusLabels, allowCustomId: false));
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
        /// <see cref="RecTimelineExportWindow.KeepChoice"/> ならそのまま、それ以外は対応先の id / レイヤー名。
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
            var dropdown = new DropdownField(targets, isCustom ? RecTimelineExportWindow.KeepChoice : current);
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
                    Refresh();
                });
                row.Add(customField);
            }

            dropdown.RegisterValueChangedCallback(evt =>
            {
                choices[recordedId] = evt.newValue;
                customField?.SetValueWithoutNotify(string.Empty);
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

        private void Refresh()
        {
            _invalidCustomIds.Clear();
            foreach (KeyValuePair<string, string> pair in _sourceChoices)
            {
                if (pair.Value != RecTimelineExportWindow.KeepChoice
                    && pair.Value != RecTimelineExportWindow.ExcludeChoice
                    && !InputSourceId.TryParse(pair.Value, out _))
                {
                    _invalidCustomIds.Add($"Source '{pair.Key}': 対応先 '{pair.Value}' は入力源 id として解釈できません。");
                }
            }

            if (_timeline == null || !_profile.HasValue)
            {
                Report = null;
                _problemBox.style.display = DisplayStyle.None;
                _problemBox.text = string.Empty;
                Changed?.Invoke();
                return;
            }

            // 解釈できない id を含むと判定ができないため、その間は前回の判定を表示しない。
            Report = _invalidCustomIds.Count == 0
                ? RecSourceRemapResolver.Analyze(_timeline, _profile.Value, BuildSourceRemap())
                : null;
            if (Report != null)
            {
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
            }

            var problems = new List<string>(_invalidCustomIds);
            if (Report != null)
            {
                problems.AddRange(RecSourceRemapResolver.DescribeProblems(Report));
            }

            if (problems.Count == 0)
            {
                _problemBox.style.display = DisplayStyle.None;
                _problemBox.text = string.Empty;
            }
            else
            {
                _problemBox.messageType = HasErrors ? HelpBoxMessageType.Error : HelpBoxMessageType.Warning;
                _problemBox.text = "未対応（書き出し前に確認してください）:\n" + string.Join("\n", problems);
                _problemBox.style.display = DisplayStyle.Flex;
            }

            Changed?.Invoke();
        }
    }
}
