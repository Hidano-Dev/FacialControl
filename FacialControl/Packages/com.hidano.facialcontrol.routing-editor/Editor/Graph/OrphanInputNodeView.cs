using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.RoutingEditor.Graph
{
    /// <summary>
    /// 未解決（無効）な入力 id を表す、ドラッグ移動可能な赤いノード。
    /// 対応する source ノードが存在しない入力を可視化し、ユーザーが配置を整理できるようにする。
    /// </summary>
    public sealed class OrphanInputNodeView : Node
    {
        public const string TypeBadgeName = "routing-orphan-input-badge";
        public const string TypeBadgeText = "未解決入力";
        private static readonly Color AccentColor = new Color(1f, 0.34f, 0.34f, 1f);
        private static readonly Color TitleBarColor = new Color(0.34f, 0.07f, 0.07f, 1f);

        public OrphanInputNodeView(DanglingEdgeData data)
        {
            Data = data;

            name = $"routing-orphan-input-{data.LayerIndex}-{data.DeclarationIndex}";
            title = string.IsNullOrEmpty(data.Id) ? "(unknown)" : data.Id;

            StyleSheet styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            // 削除 / 複製 / 改名は不可。Movable は残してドラッグ移動を許可する。
            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable | Capabilities.Renamable);

            titleContainer.style.backgroundColor = TitleBarColor;

            TypeBadge = new Label(TypeBadgeText) { name = TypeBadgeName };
            TypeBadge.style.color = AccentColor;
            TypeBadge.style.unityFontStyleAndWeight = FontStyle.Bold;
            TypeBadge.style.marginRight = 6f;
            titleButtonContainer.Add(TypeBadge);

            OutputPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Output,
                Port.Capacity.Multi,
                typeof(float));
            OutputPort.portName = "out";
            OutputPort.tooltip = data.Id;
            OutputPort.userData = data.Id;
            OutputPort.portColor = AccentColor;
            outputContainer.Add(OutputPort);

            RefreshExpandedState();
            RefreshPorts();
        }

        public DanglingEdgeData Data { get; }

        public Label TypeBadge { get; }

        public Port OutputPort { get; }

        public Color AccentWireColor => AccentColor;
    }
}
