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
    /// 合成結果（Composite Output）ノード。各レイヤーの合成順を行で示し、
    /// レイヤー間のふるまい（priority / exclusion / mask）をここで一元編集する。
    /// </summary>
    public sealed class OutputNodeView : Node
    {
        public const string NodeTitle = "Composite Output";
        public const string LayerRowClassName = "routing-output-node-row";
        public const string PriorityFieldClassName = "routing-output-priority";
        public const string ExclusionFieldClassName = "routing-output-exclusion";
        public const string MaskFieldClassName = "routing-output-mask";
        public const string ReorderButtonClassName = "routing-output-reorder";
        public const string ReorderUpButtonText = "▲";
        public const string ReorderDownButtonText = "▼";
        private static readonly UnityEngine.Color TitleBarColor = new UnityEngine.Color(0.13f, 0.26f, 0.26f, 1f);

        private readonly SerializedObject _serializedObject;
        private readonly IWiringSerializedMapper _wiringSerializedMapper;
        private readonly Dictionary<int, Port> _layerInputPorts = new Dictionary<int, Port>();

        public OutputNodeView(
            OutputNodeData outputNodeData,
            SerializedObject serializedObject,
            IWiringSerializedMapper wiringSerializedMapper)
        {
            OutputNodeData = outputNodeData ?? throw new ArgumentNullException(nameof(outputNodeData));
            _serializedObject = serializedObject ?? throw new ArgumentNullException(nameof(serializedObject));
            _wiringSerializedMapper = wiringSerializedMapper ?? throw new ArgumentNullException(nameof(wiringSerializedMapper));

            name = "routing-output-node";
            title = NodeTitle;

            StyleSheet styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable | Capabilities.Renamable);

            titleContainer.style.backgroundColor = TitleBarColor;

            for (int i = 0; i < OutputNodeData.OrderedLayers.Count; i++)
            {
                extensionContainer.Add(CreateLayerRow(i, OutputNodeData.OrderedLayers[i]));
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public OutputNodeData OutputNodeData { get; }

        /// <summary>
        /// 指定レイヤーに対応する合成入力ポートを返す。存在しなければ null。
        /// </summary>
        public Port GetLayerInputPort(int layerIndex)
        {
            return _layerInputPorts.TryGetValue(layerIndex, out Port port) ? port : null;
        }

        private VisualElement CreateLayerRow(int index, OutputLayerData layer)
        {
            var row = new VisualElement();
            row.AddToClassList(LayerRowClassName);
            row.style.marginBottom = 6f;

            var headerRow = new VisualElement();
            headerRow.style.flexDirection = FlexDirection.Row;
            headerRow.style.alignItems = Align.Center;

            Port port = InstantiatePort(
                Orientation.Horizontal,
                Direction.Input,
                Port.Capacity.Single,
                typeof(float));
            port.portName = string.Empty;
            port.tooltip = string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {layer.LayerIndex}" : layer.Name;
            port.userData = layer.LayerIndex;
            _layerInputPorts[layer.LayerIndex] = port;
            headerRow.Add(port);

            string layerName = string.IsNullOrWhiteSpace(layer.Name) ? $"Layer {layer.LayerIndex}" : layer.Name;
            var headerLabel = new Label($"{index + 1}. {layerName}");
            headerLabel.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            headerLabel.style.flexGrow = 1f;
            headerRow.Add(headerLabel);

            // 合成順（priority）を 1 段ずつ入れ替える ▲▼ ボタン。表示順の両端では無効化する。
            int displayIndex = index;
            Button moveUpButton = CreateReorderButton(
                ReorderUpButtonText,
                "1 つ上へ（合成順を前に）",
                () => MoveLayer(displayIndex, displayIndex - 1));
            moveUpButton.SetEnabled(index > 0);
            Button moveDownButton = CreateReorderButton(
                ReorderDownButtonText,
                "1 つ下へ（合成順を後ろに）",
                () => MoveLayer(displayIndex, displayIndex + 1));
            moveDownButton.SetEnabled(index < OutputNodeData.OrderedLayers.Count - 1);
            headerRow.Add(moveUpButton);
            headerRow.Add(moveDownButton);
            row.Add(headerRow);

            var priorityField = new IntegerField("Priority")
            {
                value = layer.Priority,
                isDelayed = true,
            };
            priorityField.AddToClassList(PriorityFieldClassName);

            var exclusionField = new EnumField("Exclusion Mode", layer.ExclusionMode);
            exclusionField.AddToClassList(ExclusionFieldClassName);

            var maskField = new TextField("Override Mask")
            {
                multiline = true,
                value = FormatOverrideMask(layer.OverrideMask),
            };
            maskField.AddToClassList(MaskFieldClassName);
            maskField.tooltip = "Comma or newline separated layer names.";

            int layerIndex = layer.LayerIndex;
            priorityField.RegisterValueChangedCallback(evt => ApplyLayer(
                layerIndex,
                layer.Name,
                evt.newValue,
                (ExclusionMode)exclusionField.value,
                ParseOverrideMask(maskField.value)));
            exclusionField.RegisterValueChangedCallback(evt => ApplyLayer(
                layerIndex,
                layer.Name,
                priorityField.value,
                (ExclusionMode)evt.newValue,
                ParseOverrideMask(maskField.value)));
            maskField.RegisterValueChangedCallback(evt => ApplyLayer(
                layerIndex,
                layer.Name,
                priorityField.value,
                (ExclusionMode)exclusionField.value,
                ParseOverrideMask(evt.newValue)));

            row.Add(priorityField);
            row.Add(exclusionField);
            row.Add(maskField);

            return row;
        }

        private void ApplyLayer(
            int layerIndex,
            string layerName,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask)
        {
            // Name の真実源はレイヤーノード側。ここでは構築時のスナップショット名を保持して渡し、
            // priority / exclusion / mask のみを更新する（name の空上書きを防ぐ）。
            _wiringSerializedMapper.SetLayerProperties(
                _serializedObject,
                layerIndex,
                layerName,
                priority,
                exclusionMode,
                overrideMask);
        }

        /// <summary>
        /// layerIndex 指定で 2 レイヤーの合成順（priority）を入れ替える。
        /// レイヤー出力 → Composite Output スロットの再配線（順序入れ替え）から呼ばれる。
        /// </summary>
        public void SwapLayerOrderByIndex(int layerIndexA, int layerIndexB)
        {
            if (layerIndexA == layerIndexB)
            {
                return;
            }

            if (TryFindLayer(layerIndexA, out OutputLayerData a) && TryFindLayer(layerIndexB, out OutputLayerData b))
            {
                SwapPriority(a, b);
            }
        }

        private static Button CreateReorderButton(string text, string tooltip, Action onClick)
        {
            var button = new Button(onClick)
            {
                text = text,
                tooltip = tooltip,
            };
            button.AddToClassList(ReorderButtonClassName);
            button.style.width = 22f;
            button.style.marginLeft = 2f;
            button.style.paddingLeft = 0f;
            button.style.paddingRight = 0f;
            return button;
        }

        private void MoveLayer(int fromDisplayIndex, int toDisplayIndex)
        {
            IReadOnlyList<OutputLayerData> layers = OutputNodeData.OrderedLayers;
            if (fromDisplayIndex < 0 || fromDisplayIndex >= layers.Count
                || toDisplayIndex < 0 || toDisplayIndex >= layers.Count
                || fromDisplayIndex == toDisplayIndex)
            {
                return;
            }

            SwapPriority(layers[fromDisplayIndex], layers[toDisplayIndex]);
        }

        private void SwapPriority(OutputLayerData a, OutputLayerData b)
        {
            if (a.LayerIndex == b.LayerIndex)
            {
                return;
            }

            // 元の値を先に控えてから 2 回書く（2 回目が 1 回目の書き込みを読まないように）。
            int priorityA = a.Priority;
            int priorityB = b.Priority;

            _wiringSerializedMapper.SetLayerProperties(
                _serializedObject, a.LayerIndex, a.Name, priorityB, a.ExclusionMode, a.OverrideMask);
            _wiringSerializedMapper.SetLayerProperties(
                _serializedObject, b.LayerIndex, b.Name, priorityA, b.ExclusionMode, b.OverrideMask);
        }

        private bool TryFindLayer(int layerIndex, out OutputLayerData result)
        {
            IReadOnlyList<OutputLayerData> layers = OutputNodeData.OrderedLayers;
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i].LayerIndex == layerIndex)
                {
                    result = layers[i];
                    return true;
                }
            }

            result = default;
            return false;
        }

        private static string FormatOverrideMask(IReadOnlyList<string> overrideMask)
        {
            return overrideMask == null || overrideMask.Count == 0
                ? string.Empty
                : string.Join(", ", overrideMask);
        }

        private static List<string> ParseOverrideMask(string value)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(value))
            {
                return results;
            }

            string[] tokens = value.Split(new[] { ',', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i].Trim();
                if (string.IsNullOrEmpty(token) || results.Contains(token))
                {
                    continue;
                }

                results.Add(token);
            }

            return results;
        }
    }
}
