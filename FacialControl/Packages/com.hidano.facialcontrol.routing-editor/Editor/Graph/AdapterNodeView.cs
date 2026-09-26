using System;
using System.Collections.Generic;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.RoutingEditor.Graph
{
    /// <summary>
    /// 1 つの AdapterBinding を表すノード。複数の入力源を名前付き出力ポートとして並べ、
    /// 「Adapter」バッジで概念を明示する。先頭の「All」ポートは、当該アダプターの全出力を
    /// まとめて 1 本の線でレイヤーへ一括配線するためのメタ出力。
    /// </summary>
    public sealed class AdapterNodeView : Node
    {
        public const string AllPortName = "All";
        public const string TypeBadgeName = "routing-adapter-type-badge";
        public const string TypeBadgeText = "Adapter";
        private static readonly UnityEngine.Color TitleBarColor = new UnityEngine.Color(0.20f, 0.18f, 0.30f, 1f);
        private static readonly UnityEngine.Color TypeBadgeColor = new UnityEngine.Color(0.80f, 0.74f, 1f, 1f);

        private readonly Dictionary<string, Port> _outputPorts = new Dictionary<string, Port>(StringComparer.Ordinal);

        public AdapterNodeView(AdapterNodeData data)
        {
            Data = data ?? throw new ArgumentNullException(nameof(data));

            name = $"routing-adapter-node-{Data.BindingSlug}";
            title = string.IsNullOrEmpty(Data.DisplayName) ? "Adapter" : Data.DisplayName;

            StyleSheet styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }

            capabilities &= ~(Capabilities.Deletable | Capabilities.Copiable | Capabilities.Renamable);

            // アダプターであることを一目で分かるようにタイトルバーを着色し「Adapter」バッジを付与する。
            titleContainer.style.backgroundColor = TitleBarColor;
            TypeBadge = new Label(TypeBadgeText) { name = TypeBadgeName };
            TypeBadge.style.color = TypeBadgeColor;
            TypeBadge.style.unityFontStyleAndWeight = UnityEngine.FontStyle.Bold;
            TypeBadge.style.marginRight = 6f;
            titleButtonContainer.Add(TypeBadge);

            // 「All」は各出力と同列の出力ポート。ここから線を引いてレイヤーへ繋ぐと
            // 当該アダプターの全出力をまとめて一括配線する。各出力の「上」に置く。
            AllPort = InstantiatePort(
                Orientation.Horizontal,
                Direction.Output,
                Port.Capacity.Multi,
                typeof(float));
            AllPort.portName = AllPortName;
            AllPort.tooltip = "全出力をまとめてレイヤーへ一括配線する";
            AllPort.userData = null;
            outputContainer.Add(AllPort);

            for (int i = 0; i < Data.Outputs.Count; i++)
            {
                AdapterOutputData output = Data.Outputs[i];
                Port port = InstantiatePort(
                    Orientation.Horizontal,
                    Direction.Output,
                    Port.Capacity.Multi,
                    typeof(float));
                port.portName = output.Label;
                port.tooltip = output.CanonicalId;
                port.userData = output.CanonicalId;
                _outputPorts[output.CanonicalId] = port;
                outputContainer.Add(port);
            }

            RefreshExpandedState();
            RefreshPorts();
        }

        public AdapterNodeData Data { get; }

        /// <summary>
        /// 全出力を一括配線するためのメタ出力ポート。個々の canonical id は持たない（userData は null）。
        /// </summary>
        public Port AllPort { get; }

        public Label TypeBadge { get; }

        /// <summary>
        /// 指定 canonical id に対応する出力ポートを返す。存在しなければ null。
        /// </summary>
        public Port GetOutputPort(string canonicalId)
        {
            return canonicalId != null && _outputPorts.TryGetValue(canonicalId, out Port port) ? port : null;
        }
    }
}
