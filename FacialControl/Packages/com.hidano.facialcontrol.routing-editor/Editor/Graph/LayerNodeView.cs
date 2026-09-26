using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.RoutingEditor.Graph
{
    /// <summary>
    /// レイヤーノード。名前の編集と、接続された入力源ごとのウェイト編集を担う。
    /// レイヤー間のふるまい（priority / exclusion / mask）の編集は Composite Output 側へ移設した。
    /// </summary>
    public sealed class LayerNodeView : Node
    {
        public const string NameFieldName = "routing-layer-name";
        public const string InputsHeaderName = "routing-layer-inputs-header";
        public const string InputsHeaderText = "Inputs";
        public const string WeightFieldClassName = "routing-layer-input-weight";
        public const string TypeBadgeName = "routing-layer-type-badge";
        public const string TypeBadgeText = "Layer";
        private static readonly UnityEngine.Color TitleBarColor = new UnityEngine.Color(0.16f, 0.21f, 0.33f, 1f);
        private static readonly UnityEngine.Color TypeBadgeColor = new UnityEngine.Color(0.62f, 0.76f, 1f, 1f);

        private readonly SerializedObject _serializedObject;
        private readonly IWiringSerializedMapper _wiringSerializedMapper;
        private string _layerName;
        private readonly int _priority;
        private readonly ExclusionMode _exclusionMode;
        private readonly List<string> _overrideMask;

        public LayerNodeView(
            LayerNodeData layerNodeData,
            SerializedObject serializedObject,
            IWiringSerializedMapper wiringSerializedMapper)
        {
            _serializedObject = serializedObject ?? throw new ArgumentNullException(nameof(serializedObject));
            _wiringSerializedMapper = wiringSerializedMapper ?? throw new ArgumentNullException(nameof(wiringSerializedMapper));
            LayerNodeData = layerNodeData;
            _layerName = layerNodeData.Name;
            _priority = layerNodeData.Priority;
            _exclusionMode = layerNodeData.ExclusionMode;
            _overrideMask = CopyOverrideMask(layerNodeData.OverrideMask);

            name = $"routing-layer-node-{LayerNodeData.LayerIndex}";
            title = GetNodeTitle(_layerName, LayerNodeData.LayerIndex);

            StyleSheet styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable | Capabilities.Renamable);

            // レイヤーノードであることを一目で分かるようにタイトルバーを着色し「Layer」バッジを付与する。
            titleContainer.style.backgroundColor = TitleBarColor;
            TypeBadge = new Label(TypeBadgeText) { name = TypeBadgeName };
            TypeBadge.style.color = TypeBadgeColor;
            TypeBadge.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            TypeBadge.style.marginRight = 6f;
            titleButtonContainer.Add(TypeBadge);

            InputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Input,
                Port.Capacity.Multi,
                typeof(float));
            InputPort.portName = "Inputs";
            InputPort.tooltip = $"Layer {LayerNodeData.LayerIndex}";
            InputPort.userData = LayerNodeData.LayerIndex;
            inputContainer.Add(InputPort);

            // 合成出力（Composite Output）へブレンドされることを線で示すための出力ポート。
            OutputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Output,
                Port.Capacity.Single,
                typeof(float));
            OutputPort.portName = "Blend";
            OutputPort.tooltip = "Composite Output へ合成される";
            OutputPort.userData = LayerNodeData.LayerIndex;
            outputContainer.Add(OutputPort);

            NameField = new TextField("Name")
            {
                name = NameFieldName,
                value = _layerName,
                isDelayed = true,
            };
            NameField.RegisterValueChangedCallback(evt => ApplyLayerName(evt.newValue));
            extensionContainer.Add(NameField);

            BuildInputWeightRows();

            RefreshExpandedState();
            RefreshPorts();
        }

        public LayerNodeData LayerNodeData { get; }

        public TextField NameField { get; }

        public Port InputPort { get; }

        public Port OutputPort { get; }

        public Label TypeBadge { get; }

        private void BuildInputWeightRows()
        {
            IReadOnlyList<LayerInputData> inputs = LayerNodeData.Inputs;
            if (inputs == null || inputs.Count == 0)
            {
                return;
            }

            var header = new Label(InputsHeaderText) { name = InputsHeaderName };
            header.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            header.style.marginTop = 4f;
            extensionContainer.Add(header);

            for (int i = 0; i < inputs.Count; i++)
            {
                extensionContainer.Add(CreateInputWeightRow(inputs[i]));
            }
        }

        private VisualElement CreateInputWeightRow(LayerInputData input)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;

            var label = new Label(input.Label);
            label.style.flexGrow = 1f;
            label.style.marginRight = 6f;
            row.Add(label);

            var weightField = new FloatField
            {
                name = $"routing-layer-weight-{input.CanonicalId}",
                value = input.Weight,
                isDelayed = true,
                userData = input.CanonicalId,
            };
            weightField.AddToClassList(WeightFieldClassName);
            weightField.style.width = 60f;
            weightField.RegisterValueChangedCallback(evt => ApplyInputWeight(input.CanonicalId, evt.newValue));
            row.Add(weightField);

            return row;
        }

        private void ApplyLayerName(string layerName)
        {
            _layerName = layerName ?? string.Empty;

            // SetLayerProperties は name/priority/exclusion/mask を一括で書くため、
            // この場で編集していない priority/exclusion/mask は構築時のスナップショットを保持して渡す。
            _wiringSerializedMapper.SetLayerProperties(
                _serializedObject,
                LayerNodeData.LayerIndex,
                _layerName,
                _priority,
                _exclusionMode,
                _overrideMask);

            title = GetNodeTitle(_layerName, LayerNodeData.LayerIndex);
        }

        private void ApplyInputWeight(string canonicalId, float weight)
        {
            _wiringSerializedMapper.SetWeight(
                _serializedObject,
                LayerNodeData.LayerIndex,
                canonicalId,
                UnityEngine.Mathf.Clamp01(weight));
        }

        private static string GetNodeTitle(string layerName, int layerIndex)
        {
            return string.IsNullOrWhiteSpace(layerName)
                ? $"Layer {layerIndex}"
                : layerName;
        }

        private static List<string> CopyOverrideMask(IReadOnlyList<string> overrideMask)
        {
            var copy = new List<string>();
            if (overrideMask == null)
            {
                return copy;
            }

            for (int i = 0; i < overrideMask.Count; i++)
            {
                string value = overrideMask[i];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    copy.Add(value);
                }
            }

            return copy;
        }
    }
}
