using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    /// <summary>
    /// アダプターノードの 1 出力ポート（= 1 canonical id）を表す。
    /// </summary>
    public readonly struct AdapterOutputData
    {
        public AdapterOutputData(string canonicalId, string label)
        {
            CanonicalId = canonicalId ?? string.Empty;
            Label = label ?? string.Empty;
        }

        public string CanonicalId { get; }

        public string Label { get; }
    }

    /// <summary>
    /// 1 つの AdapterBinding を表すノード。複数の出力ポート（入力源）を束ねる。
    /// </summary>
    public sealed class AdapterNodeData
    {
        public AdapterNodeData(
            string bindingSlug,
            string displayName,
            bool supportsAutoWire,
            IReadOnlyList<AdapterOutputData> outputs)
        {
            BindingSlug = bindingSlug ?? string.Empty;
            DisplayName = string.IsNullOrEmpty(displayName) ? BindingSlug : displayName;
            SupportsAutoWire = supportsAutoWire;
            Outputs = Copy(outputs);
        }

        public string BindingSlug { get; }

        public string DisplayName { get; }

        /// <summary>
        /// 「overlay レイヤーへ一括配線」が意味を持つ binding（default layer inputs 系）のみ true。
        /// InputSystem のような宣言入力系は no-op なのでボタンを出さない。
        /// </summary>
        public bool SupportsAutoWire { get; }

        public IReadOnlyList<AdapterOutputData> Outputs { get; }

        private static IReadOnlyList<AdapterOutputData> Copy(IReadOnlyList<AdapterOutputData> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<AdapterOutputData>();
            }

            var copy = new AdapterOutputData[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }

    /// <summary>
    /// レイヤーに接続された 1 入力源（canonical id）のウェイト編集用データ。
    /// </summary>
    public readonly struct LayerInputData
    {
        public LayerInputData(string canonicalId, string label, float weight)
            : this(canonicalId, label, weight, false)
        {
        }

        public LayerInputData(string canonicalId, string label, float weight, bool isAutoDeclared)
        {
            CanonicalId = canonicalId ?? string.Empty;
            Label = string.IsNullOrEmpty(label) ? CanonicalId : label;
            Weight = weight;
            IsAutoDeclared = isAutoDeclared;
        }

        public string CanonicalId { get; }

        public string Label { get; }

        public float Weight { get; }

        /// <summary>
        /// binding の対象レイヤー指定によりランタイムが自動で補う入力源なら true（Profile アセットには宣言が無い）。
        /// </summary>
        public bool IsAutoDeclared { get; }
    }

    public readonly struct LayerNodeData
    {
        public LayerNodeData(
            int layerIndex,
            string name,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask)
            : this(layerIndex, name, priority, exclusionMode, overrideMask, Array.Empty<LayerInputData>())
        {
        }

        public LayerNodeData(
            int layerIndex,
            string name,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask,
            IReadOnlyList<LayerInputData> inputs)
        {
            LayerIndex = layerIndex;
            Name = name ?? string.Empty;
            Priority = priority;
            ExclusionMode = exclusionMode;
            OverrideMask = CopyStrings(overrideMask);
            Inputs = CopyInputs(inputs);
        }

        public int LayerIndex { get; }

        public string Name { get; }

        public int Priority { get; }

        public ExclusionMode ExclusionMode { get; }

        public IReadOnlyList<string> OverrideMask { get; }

        /// <summary>このレイヤーに接続された入力源（ウェイト一覧表示用）。宣言順。</summary>
        public IReadOnlyList<LayerInputData> Inputs { get; }

        private static IReadOnlyList<string> CopyStrings(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i] ?? string.Empty;
            }

            return copy;
        }

        private static IReadOnlyList<LayerInputData> CopyInputs(IReadOnlyList<LayerInputData> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<LayerInputData>();
            }

            var copy = new LayerInputData[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }

    public readonly struct OutputLayerData
    {
        public OutputLayerData(int layerIndex, string name, int priority, IReadOnlyList<string> overrideMask)
            : this(layerIndex, name, priority, ExclusionMode.LastWins, overrideMask)
        {
        }

        public OutputLayerData(
            int layerIndex,
            string name,
            int priority,
            ExclusionMode exclusionMode,
            IReadOnlyList<string> overrideMask)
        {
            LayerIndex = layerIndex;
            Name = name ?? string.Empty;
            Priority = priority;
            ExclusionMode = exclusionMode;
            OverrideMask = CopyStrings(overrideMask);
        }

        public int LayerIndex { get; }

        public string Name { get; }

        public int Priority { get; }

        public ExclusionMode ExclusionMode { get; }

        public IReadOnlyList<string> OverrideMask { get; }

        private static IReadOnlyList<string> CopyStrings(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<string>();
            }

            var copy = new string[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i] ?? string.Empty;
            }

            return copy;
        }
    }

    public sealed class OutputNodeData
    {
        public OutputNodeData(IReadOnlyList<OutputLayerData> orderedLayers)
        {
            OrderedLayers = Copy(orderedLayers);
        }

        public IReadOnlyList<OutputLayerData> OrderedLayers { get; }

        private static IReadOnlyList<OutputLayerData> Copy(IReadOnlyList<OutputLayerData> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<OutputLayerData>();
            }

            var copy = new OutputLayerData[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }

    public readonly struct WiringEdgeData
    {
        public WiringEdgeData(int layerIndex, string canonicalId, float weight)
            : this(layerIndex, canonicalId, weight, false)
        {
        }

        public WiringEdgeData(int layerIndex, string canonicalId, float weight, bool isAutoDeclared)
        {
            LayerIndex = layerIndex;
            CanonicalId = canonicalId ?? string.Empty;
            Weight = weight;
            IsAutoDeclared = isAutoDeclared;
        }

        public int LayerIndex { get; }

        public string CanonicalId { get; }

        public float Weight { get; }

        /// <summary>
        /// binding の対象レイヤー指定によりランタイムが自動で補う配線なら true。
        /// Profile アセットに宣言が無いため、削除・繋ぎ替え・weight 編集の対象にしない。
        /// </summary>
        public bool IsAutoDeclared { get; }
    }

    public readonly struct DanglingEdgeData
    {
        public DanglingEdgeData(int layerIndex, int declarationIndex, string id)
        {
            LayerIndex = layerIndex;
            DeclarationIndex = declarationIndex;
            Id = id ?? string.Empty;
        }

        public int LayerIndex { get; }

        public int DeclarationIndex { get; }

        public string Id { get; }
    }

    public sealed class RoutingGraphModel
    {
        public RoutingGraphModel(
            IReadOnlyList<AdapterNodeData> adapterNodes,
            IReadOnlyList<LayerNodeData> layerNodes,
            OutputNodeData outputNode,
            IReadOnlyList<WiringEdgeData> edges,
            IReadOnlyList<DanglingEdgeData> invalidEdges)
        {
            AdapterNodes = Copy(adapterNodes);
            LayerNodes = Copy(layerNodes);
            OutputNode = outputNode ?? new OutputNodeData(Array.Empty<OutputLayerData>());
            Edges = Copy(edges);
            InvalidEdges = Copy(invalidEdges);
        }

        public IReadOnlyList<AdapterNodeData> AdapterNodes { get; }

        public IReadOnlyList<LayerNodeData> LayerNodes { get; }

        public OutputNodeData OutputNode { get; }

        public IReadOnlyList<WiringEdgeData> Edges { get; }

        public IReadOnlyList<DanglingEdgeData> InvalidEdges { get; }

        private static IReadOnlyList<T> Copy<T>(IReadOnlyList<T> values)
        {
            if (values == null || values.Count == 0)
            {
                return Array.Empty<T>();
            }

            var copy = new T[values.Count];
            for (int i = 0; i < values.Count; i++)
            {
                copy[i] = values[i];
            }

            return copy;
        }
    }
}
