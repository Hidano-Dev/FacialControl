using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Editor.Windows.Routing.Logic
{
    public interface IRoutingGraphModelBuilder
    {
        RoutingGraphModel Build(FacialCharacterProfileSO profile);
    }

    public sealed class RoutingGraphModelBuilder : IRoutingGraphModelBuilder
    {
        private readonly ISourcePortEnumerator _sourcePortEnumerator;
        private readonly IInvalidIdValidator _invalidIdValidator;

        public RoutingGraphModelBuilder()
            : this(new SourcePortEnumerator(), new InvalidIdValidator())
        {
        }

        public RoutingGraphModelBuilder(
            ISourcePortEnumerator sourcePortEnumerator,
            IInvalidIdValidator invalidIdValidator)
        {
            _sourcePortEnumerator = sourcePortEnumerator ?? throw new ArgumentNullException(nameof(sourcePortEnumerator));
            _invalidIdValidator = invalidIdValidator ?? throw new ArgumentNullException(nameof(invalidIdValidator));
        }

        public RoutingGraphModel Build(FacialCharacterProfileSO profile)
        {
            if (profile == null)
            {
                throw new ArgumentNullException(nameof(profile));
            }

            IReadOnlyList<LayerDefinitionSerializable> layers =
                profile.Layers != null
                    ? profile.Layers
                    : Array.Empty<LayerDefinitionSerializable>();
            string[] layerNames = BuildLayerNames(layers);
            IReadOnlyList<AdapterBindingBase> bindings =
                profile.AdapterBindings != null
                    ? profile.AdapterBindings
                    : Array.Empty<AdapterBindingBase>();
            IReadOnlyList<SourcePort> sourcePorts =
                _sourcePortEnumerator.Enumerate(bindings, layerNames);
            ISet<string> validCanonicalIds =
                _sourcePortEnumerator.EnumerateCanonicalIds(bindings, layerNames);
            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations =
                _invalidIdValidator.Validate(profile, validCanonicalIds);

            AdapterNodeData[] adapterNodes = BuildAdapterNodes(sourcePorts, bindings);
            WiringEdgeData[] edges = BuildEdges(layers, bindings, invalidDeclarations, validCanonicalIds);
            LayerNodeData[] layerNodes = BuildLayerNodes(layers, edges, sourcePorts);
            DanglingEdgeData[] invalidEdges = BuildInvalidEdges(invalidDeclarations);
            OutputNodeData outputNode = BuildOutputNode(layerNodes);

            return new RoutingGraphModel(adapterNodes, layerNodes, outputNode, edges, invalidEdges);
        }

        private static string[] BuildLayerNames(IReadOnlyList<LayerDefinitionSerializable> layers)
        {
            if (layers == null || layers.Count == 0)
            {
                return Array.Empty<string>();
            }

            var layerNames = new string[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                layerNames[i] = layers[i]?.name ?? string.Empty;
            }

            return layerNames;
        }

        /// <summary>
        /// SourcePort を BindingSlug 単位にグルーピングし、1 binding = 1 アダプターノードへ変換する。
        /// グループ順は SourcePort の登場順（= binding 列挙順）を保つ。
        /// </summary>
        private static AdapterNodeData[] BuildAdapterNodes(
            IReadOnlyList<SourcePort> sourcePorts,
            IReadOnlyList<AdapterBindingBase> bindings)
        {
            if (sourcePorts == null || sourcePorts.Count == 0)
            {
                return Array.Empty<AdapterNodeData>();
            }

            Dictionary<string, bool> autoWireBySlug = BuildAutoWireSupportMap(bindings);

            var orderedSlugs = new List<string>();
            var outputsBySlug = new Dictionary<string, List<AdapterOutputData>>(StringComparer.Ordinal);

            for (int i = 0; i < sourcePorts.Count; i++)
            {
                SourcePort sourcePort = sourcePorts[i];
                string slug = sourcePort.BindingSlug ?? string.Empty;
                if (!outputsBySlug.TryGetValue(slug, out List<AdapterOutputData> outputs))
                {
                    outputs = new List<AdapterOutputData>();
                    outputsBySlug.Add(slug, outputs);
                    orderedSlugs.Add(slug);
                }

                outputs.Add(new AdapterOutputData(sourcePort.CanonicalId, sourcePort.Label));
            }

            var adapterNodes = new AdapterNodeData[orderedSlugs.Count];
            for (int i = 0; i < orderedSlugs.Count; i++)
            {
                string slug = orderedSlugs[i];
                bool supportsAutoWire = autoWireBySlug.TryGetValue(slug, out bool value) && value;
                adapterNodes[i] = new AdapterNodeData(slug, slug, supportsAutoWire, outputsBySlug[slug]);
            }

            return adapterNodes;
        }

        private static Dictionary<string, bool> BuildAutoWireSupportMap(IReadOnlyList<AdapterBindingBase> bindings)
        {
            var map = new Dictionary<string, bool>(StringComparer.Ordinal);
            if (bindings == null)
            {
                return map;
            }

            for (int i = 0; i < bindings.Count; i++)
            {
                AdapterBindingBase binding = bindings[i];
                if (binding == null)
                {
                    continue;
                }

                string slug = binding.Slug ?? string.Empty;
                bool supports = binding is IAdapterBindingDefaultLayerInputs;
                map[slug] = map.TryGetValue(slug, out bool existing) ? existing || supports : supports;
            }

            return map;
        }

        private static LayerNodeData[] BuildLayerNodes(
            IReadOnlyList<LayerDefinitionSerializable> layers,
            IReadOnlyList<WiringEdgeData> edges,
            IReadOnlyList<SourcePort> sourcePorts)
        {
            if (layers == null || layers.Count == 0)
            {
                return Array.Empty<LayerNodeData>();
            }

            Dictionary<string, string> labelByCanonicalId = BuildLabelMap(sourcePorts);

            var inputsByLayer = new List<LayerInputData>[layers.Count];
            for (int i = 0; i < edges.Count; i++)
            {
                WiringEdgeData edge = edges[i];
                if (edge.LayerIndex < 0 || edge.LayerIndex >= layers.Count)
                {
                    continue;
                }

                (inputsByLayer[edge.LayerIndex] ??= new List<LayerInputData>()).Add(
                    new LayerInputData(
                        edge.CanonicalId,
                        ResolveLabel(labelByCanonicalId, edge.CanonicalId),
                        edge.Weight,
                        edge.IsAutoDeclared));
            }

            var layerNodes = new LayerNodeData[layers.Count];
            for (int i = 0; i < layers.Count; i++)
            {
                LayerDefinitionSerializable layer = layers[i];
                layerNodes[i] = new LayerNodeData(
                    i,
                    layer?.name ?? string.Empty,
                    layer?.priority ?? 0,
                    layer?.exclusionMode ?? 0,
                    layer?.layerOverrideMask,
                    (IReadOnlyList<LayerInputData>)inputsByLayer[i] ?? Array.Empty<LayerInputData>());
            }

            return layerNodes;
        }

        private static Dictionary<string, string> BuildLabelMap(IReadOnlyList<SourcePort> sourcePorts)
        {
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (sourcePorts == null)
            {
                return map;
            }

            for (int i = 0; i < sourcePorts.Count; i++)
            {
                SourcePort sourcePort = sourcePorts[i];
                map[sourcePort.CanonicalId] = sourcePort.Label;
            }

            return map;
        }

        private static string ResolveLabel(Dictionary<string, string> labelByCanonicalId, string canonicalId)
        {
            if (labelByCanonicalId.TryGetValue(canonicalId, out string label) && !string.IsNullOrEmpty(label))
            {
                return label;
            }

            if (string.IsNullOrEmpty(canonicalId))
            {
                return string.Empty;
            }

            int separatorIndex = canonicalId.LastIndexOf(':');
            return separatorIndex < 0 || separatorIndex >= canonicalId.Length - 1
                ? canonicalId
                : canonicalId.Substring(separatorIndex + 1);
        }

        /// <summary>
        /// Profile アセットの宣言から配線を作り、続けてランタイムが自動で補う配線（<see cref="IAdapterBindingTargetLayerInput"/>）を足す。
        /// </summary>
        private static WiringEdgeData[] BuildEdges(
            IReadOnlyList<LayerDefinitionSerializable> layers,
            IReadOnlyList<AdapterBindingBase> bindings,
            IReadOnlyList<InvalidDeclarationRef> invalidDeclarations,
            ISet<string> validCanonicalIds)
        {
            if (layers == null || layers.Count == 0)
            {
                return Array.Empty<WiringEdgeData>();
            }

            var invalidSet = new HashSet<(int layerIndex, int declarationIndex)>();
            for (int i = 0; i < invalidDeclarations.Count; i++)
            {
                InvalidDeclarationRef invalid = invalidDeclarations[i];
                invalidSet.Add((invalid.LayerIndex, invalid.DeclarationIndex));
            }

            var edges = new List<WiringEdgeData>();
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                IList<InputSourceDeclarationSerializable> declarations = layers[layerIndex]?.inputSources;
                if (declarations == null)
                {
                    continue;
                }

                for (int declarationIndex = 0; declarationIndex < declarations.Count; declarationIndex++)
                {
                    if (invalidSet.Contains((layerIndex, declarationIndex)))
                    {
                        continue;
                    }

                    InputSourceDeclarationSerializable declaration = declarations[declarationIndex];
                    edges.Add(new WiringEdgeData(
                        layerIndex,
                        declaration?.id ?? string.Empty,
                        declaration?.weight ?? 0f));
                }
            }

            AddAutoDeclaredEdges(layers, bindings, validCanonicalIds, edges);
            return edges.ToArray();
        }

        /// <summary>
        /// ランタイムが自動で補う配線を <see cref="TargetLayerInputSourceResolver"/> と同じ規則で足す。
        /// ランタイムと同じレイヤー（変換で読み捨てられるレイヤーを除いたもの）と宣言で判定し、
        /// 結果を Profile アセットのレイヤーインデックスへ戻す。描画できる入力源 id のものだけを足す。
        /// </summary>
        private static void AddAutoDeclaredEdges(
            IReadOnlyList<LayerDefinitionSerializable> layers,
            IReadOnlyList<AdapterBindingBase> bindings,
            ISet<string> validCanonicalIds,
            List<WiringEdgeData> edges)
        {
            int[] sourceIndices = FacialCharacterProfileConverter.GetConvertedLayerSourceIndices(layers);
            var layerNames = new string[sourceIndices.Length];
            var declaredIds = new List<string>();
            for (int i = 0; i < sourceIndices.Length; i++)
            {
                LayerDefinitionSerializable layer = layers[sourceIndices[i]];
                layerNames[i] = layer.name;
                IList<InputSourceDeclarationSerializable> declarations = layer.inputSources;
                if (declarations == null)
                {
                    continue;
                }

                for (int d = 0; d < declarations.Count; d++)
                {
                    string id = declarations[d]?.id;
                    if (!string.IsNullOrWhiteSpace(id))
                    {
                        declaredIds.Add(id);
                    }
                }
            }

            IReadOnlyList<TargetLayerInputAssignment> assignments =
                TargetLayerInputSourceResolver.ResolveConfigured(layerNames, declaredIds, bindings, null);
            for (int i = 0; i < assignments.Count; i++)
            {
                TargetLayerInputAssignment assignment = assignments[i];
                if (!validCanonicalIds.Contains(assignment.InputSourceId))
                {
                    continue;
                }

                edges.Add(new WiringEdgeData(
                    sourceIndices[assignment.LayerIndex],
                    assignment.InputSourceId,
                    TargetLayerInputSourceResolver.AutoDeclarationWeight,
                    isAutoDeclared: true));
            }
        }

        private static DanglingEdgeData[] BuildInvalidEdges(IReadOnlyList<InvalidDeclarationRef> invalidDeclarations)
        {
            if (invalidDeclarations == null || invalidDeclarations.Count == 0)
            {
                return Array.Empty<DanglingEdgeData>();
            }

            var invalidEdges = new DanglingEdgeData[invalidDeclarations.Count];
            for (int i = 0; i < invalidDeclarations.Count; i++)
            {
                InvalidDeclarationRef invalid = invalidDeclarations[i];
                invalidEdges[i] = new DanglingEdgeData(invalid.LayerIndex, invalid.DeclarationIndex, invalid.Id);
            }

            return invalidEdges;
        }

        private static OutputNodeData BuildOutputNode(IReadOnlyList<LayerNodeData> layerNodes)
        {
            if (layerNodes == null || layerNodes.Count == 0)
            {
                return new OutputNodeData(Array.Empty<OutputLayerData>());
            }

            var orderedLayers = new List<OutputLayerData>(layerNodes.Count);
            for (int i = 0; i < layerNodes.Count; i++)
            {
                LayerNodeData layer = layerNodes[i];
                orderedLayers.Add(new OutputLayerData(
                    layer.LayerIndex,
                    layer.Name,
                    layer.Priority,
                    layer.ExclusionMode,
                    layer.OverrideMask));
            }

            orderedLayers.Sort(static (left, right) =>
            {
                int priorityCompare = left.Priority.CompareTo(right.Priority);
                if (priorityCompare != 0)
                {
                    return priorityCompare;
                }

                return left.LayerIndex.CompareTo(right.LayerIndex);
            });

            return new OutputNodeData(orderedLayers);
        }
    }
}
