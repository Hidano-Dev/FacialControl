using System;
using Hidano.FacialControl.Editor.Common;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using UnityEditor.Experimental.GraphView;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.RoutingEditor.Graph
{
    /// <summary>
    /// アダプター出力ポートとレイヤー入力ポートを結ぶ配線エッジ。
    /// 選択・削除・端点ドラッグでの繋ぎ替えを許可する。ウェイト編集はレイヤーノード側で行う。
    /// </summary>
    public sealed class RoutingEdge : Edge
    {
        public RoutingEdge(Port outputPort, Port inputPort, WiringEdgeData edgeData)
        {
            if (outputPort == null)
            {
                throw new ArgumentNullException(nameof(outputPort));
            }

            if (inputPort == null)
            {
                throw new ArgumentNullException(nameof(inputPort));
            }

            EdgeData = edgeData;

            output = outputPort;
            input = inputPort;
            output.Connect(this);
            input.Connect(this);

            // 既定で Selectable / Deletable は有効。複製・改名のみ不可にする。
            capabilities &= ~(Capabilities.Copiable | Capabilities.Renamable);

            StyleSheet styleSheet = FacialControlStyles.Load();
            if (styleSheet != null)
            {
                styleSheets.Add(styleSheet);
            }
        }

        /// <summary>この配線が表す宣言（layerIndex / canonicalId / weight）。繋ぎ替え時の weight 引き継ぎに使う。</summary>
        public WiringEdgeData EdgeData { get; }
    }
}
