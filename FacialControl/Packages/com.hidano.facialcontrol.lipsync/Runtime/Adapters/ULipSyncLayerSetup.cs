using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.LipSync.Adapters
{
    /// <summary>
    /// 発話ゲートの対象レイヤー（uLipSync の入力源を <c>inputSources</c> に宣言したレイヤー）の解決と、
    /// キャプチャ系入力源とのレイヤー構成の検査。Runtime（<see cref="ULipSyncAdapterBinding"/>）と
    /// Inspector の警告表示で共有する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// uLipSync の入力源 id は <c>lipsync-overlay:{a|i|u|e|o}</c>（固定 prefix）で登録される。
    /// 旧構成の binding slug（既定 <c>ulipsync</c>）を宣言したレイヤーも uLipSync のレイヤーとして扱う。
    /// </para>
    /// <para>
    /// 発話ゲートがレイヤー weight を書くのは、uLipSync の入力源<b>だけ</b>を宣言したレイヤーに限る。
    /// binding 追加時に自動で作られる <c>overlay</c> レイヤーのように他の入力源（<c>overlay:{slot}</c> 等）と
    /// 同居するレイヤーの weight を書くと、無言の間それらも一緒に消えるため。同居レイヤーは入力源の
    /// 有効 / 無効だけで制御し、Inspector で警告する。
    /// </para>
    /// </remarks>
    public static class ULipSyncLayerSetup
    {
        /// <summary>検査対象の 1 レイヤー。</summary>
        public readonly struct Layer
        {
            public readonly string Name;
            public readonly int Priority;
            public readonly IReadOnlyList<string> InputSourceIds;

            public Layer(string name, int priority, IReadOnlyList<string> inputSourceIds)
            {
                Name = name;
                Priority = priority;
                InputSourceIds = inputSourceIds ?? Array.Empty<string>();
            }
        }

        /// <summary>FacialProfile のレイヤーと <c>inputSources</c> 宣言から検査対象を組み立てる。</summary>
        public static Layer[] FromProfile(in FacialProfile profile)
        {
            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            ReadOnlySpan<InputSourceDeclaration[]> declarations = profile.LayerInputSources.Span;
            var result = new Layer[layers.Length];
            for (int i = 0; i < layers.Length; i++)
            {
                InputSourceDeclaration[] decls = i < declarations.Length ? declarations[i] : null;
                var ids = new string[decls != null ? decls.Length : 0];
                for (int j = 0; j < ids.Length; j++)
                {
                    ids[j] = decls[j].Id;
                }

                result[i] = new Layer(layers[i].Name, layers[i].Priority, ids);
            }

            return result;
        }

        /// <summary>
        /// <see cref="IAdapterBindingTargetLayerInput"/> を実装する binding（OSC Receiver 等）が起動時にレイヤーへ補う
        /// 入力源を足した検査対象を返す。補う規則は <see cref="TargetLayerInputSourceResolver.ResolveConfigured"/> と同じ。
        /// </summary>
        /// <remarks>
        /// 手動宣言だけを見ると、対象レイヤー未指定の OSC Receiver が先頭の uLipSync 専用レイヤーへ補われたときに
        /// 「uLipSync だけのレイヤー」と誤判定し、無言の間キャプチャまで消してしまうため。
        /// </remarks>
        public static Layer[] AddTargetLayerInputs(Layer[] layers, IReadOnlyList<AdapterBindingBase> bindings)
        {
            if (layers == null || layers.Length == 0 || bindings == null || bindings.Count == 0)
            {
                return layers;
            }

            var layerNames = new string[layers.Length];
            var declaredIds = new List<string>();
            for (int i = 0; i < layers.Length; i++)
            {
                layerNames[i] = layers[i].Name;
                for (int j = 0; j < layers[i].InputSourceIds.Count; j++)
                {
                    if (!string.IsNullOrEmpty(layers[i].InputSourceIds[j]))
                    {
                        declaredIds.Add(layers[i].InputSourceIds[j]);
                    }
                }
            }

            IReadOnlyList<TargetLayerInputAssignment> assignments =
                TargetLayerInputSourceResolver.ResolveConfigured(layerNames, declaredIds, bindings, null);
            if (assignments.Count == 0)
            {
                return layers;
            }

            var result = new Layer[layers.Length];
            Array.Copy(layers, result, layers.Length);
            for (int i = 0; i < assignments.Count; i++)
            {
                int index = assignments[i].LayerIndex;
                if (index < 0 || index >= result.Length)
                {
                    continue;
                }

                var ids = new List<string>(result[index].InputSourceIds) { assignments[i].InputSourceId };
                result[index] = new Layer(result[index].Name, result[index].Priority, ids);
            }

            return result;
        }

        /// <summary>入力源 id が slug そのもの、または <c>slug:sub</c> か。</summary>
        public static bool DeclaresSlug(string inputSourceId, string slug)
        {
            if (string.IsNullOrEmpty(inputSourceId) || string.IsNullOrEmpty(slug))
            {
                return false;
            }

            if (string.Equals(inputSourceId, slug, StringComparison.Ordinal))
            {
                return true;
            }

            return inputSourceId.Length > slug.Length
                && inputSourceId[slug.Length] == ':'
                && inputSourceId.StartsWith(slug, StringComparison.Ordinal);
        }

        /// <summary>レイヤーが uLipSync の入力源（<c>lipsync-overlay:*</c> または binding slug）を宣言しているか。</summary>
        public static bool DeclaresULipSync(in Layer layer, string bindingSlug)
        {
            for (int i = 0; i < layer.InputSourceIds.Count; i++)
            {
                string id = layer.InputSourceIds[i];
                if (DeclaresSlug(id, LipSyncPhonemeOverlayInputSource.SlugPrefix) || DeclaresSlug(id, bindingSlug))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>レイヤーが uLipSync の入力源だけを宣言しているか（空のレイヤーは false）。</summary>
        public static bool DeclaresOnlyULipSync(in Layer layer, string bindingSlug)
        {
            bool any = false;
            for (int i = 0; i < layer.InputSourceIds.Count; i++)
            {
                string id = layer.InputSourceIds[i];
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                if (!DeclaresSlug(id, LipSyncPhonemeOverlayInputSource.SlugPrefix) && !DeclaresSlug(id, bindingSlug))
                {
                    return false;
                }

                any = true;
            }

            return any;
        }

        /// <summary>
        /// 発話ゲートがレイヤー weight を書く対象（uLipSync の入力源だけを宣言したレイヤー）の名前を
        /// 定義順に返す（重複名は 1 回）。
        /// </summary>
        public static string[] FindGateLayerNames(IReadOnlyList<Layer> layers, string bindingSlug)
        {
            if (layers == null || layers.Count == 0)
            {
                return Array.Empty<string>();
            }

            var names = new List<string>();
            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                if (!string.IsNullOrEmpty(layer.Name)
                    && DeclaresOnlyULipSync(in layer, bindingSlug)
                    && !names.Contains(layer.Name))
                {
                    names.Add(layer.Name);
                }
            }

            return names.ToArray();
        }

        /// <summary>
        /// レイヤー構成の警告を返す。警告が無ければ空。
        /// </summary>
        /// <param name="layers">検査対象のレイヤー。</param>
        /// <param name="bindingSlug">uLipSync binding の slug。</param>
        /// <param name="captureSlugs">同じキャラクターの iFacialMocap Receiver / OSC Receiver binding の slug。</param>
        public static List<string> CollectWarnings(
            IReadOnlyList<Layer> layers,
            string bindingSlug,
            IReadOnlyList<string> captureSlugs)
        {
            var warnings = new List<string>();
            bool hasGateLayer = false;
            int maxGatePriority = int.MinValue;
            if (layers != null)
            {
                for (int i = 0; i < layers.Count; i++)
                {
                    Layer layer = layers[i];
                    if (DeclaresULipSync(in layer, bindingSlug))
                    {
                        hasGateLayer = true;
                        if (layer.Priority > maxGatePriority)
                        {
                            maxGatePriority = layer.Priority;
                        }
                    }
                }
            }

            if (!hasGateLayer)
            {
                warnings.Add(
                    $"uLipSync の入力源（{LipSyncPhonemeOverlayInputSource.SlugPrefix}:a〜o）を inputSources に宣言したレイヤーがありません。"
                    + "発話ゲートはレイヤー weight を書けず、入力源の有効 / 無効だけで制御します。");
                return warnings;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                if (DeclaresULipSync(in layer, bindingSlug) && !DeclaresOnlyULipSync(in layer, bindingSlug))
                {
                    warnings.Add(
                        $"レイヤー '{layer.Name}' には uLipSync 以外の入力源も宣言されているため、発話ゲートはこのレイヤーの weight を書きません"
                        + "（入力源の有効 / 無効だけで制御し、Attack / Release は効きません）。uLipSync の入力源だけを宣言した専用レイヤーに分けてください。");
                }
            }

            if (captureSlugs == null)
            {
                return warnings;
            }

            for (int i = 0; i < layers.Count; i++)
            {
                Layer layer = layers[i];
                if (layer.Priority < maxGatePriority)
                {
                    continue;
                }

                for (int s = 0; s < captureSlugs.Count; s++)
                {
                    string captureSlug = captureSlugs[s];
                    if (!DeclaresAny(in layer, captureSlug))
                    {
                        continue;
                    }

                    warnings.Add(
                        DeclaresULipSync(in layer, bindingSlug)
                            ? $"レイヤー '{layer.Name}' に uLipSync とキャプチャ入力源 '{captureSlug}' が同居しています。"
                              + "発話ゲートがレイヤー weight を書き換えるとキャプチャも一緒に消えるため、キャプチャは uLipSync より低い priority の別レイヤーに分けてください。"
                            : $"キャプチャ入力源 '{captureSlug}' のレイヤー '{layer.Name}'（priority {layer.Priority}）が uLipSync のレイヤー（priority {maxGatePriority}）以上の priority です。"
                              + "発話中に uLipSync の口が上書きされるため、キャプチャのレイヤーを uLipSync より低い priority にしてください。");
                }
            }

            return warnings;
        }

        private static bool DeclaresAny(in Layer layer, string slug)
        {
            for (int i = 0; i < layer.InputSourceIds.Count; i++)
            {
                if (DeclaresSlug(layer.InputSourceIds[i], slug))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
