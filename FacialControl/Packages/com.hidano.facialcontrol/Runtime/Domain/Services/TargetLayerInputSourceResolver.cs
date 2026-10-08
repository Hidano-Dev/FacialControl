using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Domain.Services
{
    /// <summary>
    /// <see cref="IAdapterBindingTargetLayerInput"/> を実装する binding の入力源を、
    /// プロファイルのレイヤー宣言へ起動時に補った結果を求める静的サービス。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 補う規則:
    /// <list type="bullet">
    /// <item>入力源 id がいずれかのレイヤーに宣言済みなら何もしない（手動宣言を優先し、同じ id を 2 回合成しない）</item>
    /// <item>対象レイヤー名が未指定（null / 空白）ならプロファイルの先頭レイヤーへ補う</item>
    /// <item>対象レイヤー名がプロファイルに無い、またはレイヤーが 1 つも無い場合は補わず警告を返す</item>
    /// <item>補う宣言の weight は <see cref="AutoDeclarationWeight"/>、options は未指定</item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="FacialProfile"/> は変更しない。結果は Adapter 層がレイヤー入力源の解決に使う。
    /// </para>
    /// </remarks>
    public static class TargetLayerInputSourceResolver
    {
        /// <summary>自動で補う宣言の weight。</summary>
        public const float AutoDeclarationWeight = 1f;

        /// <summary>
        /// プロファイルのレイヤー宣言に、binding の入力源を補った宣言配列を返す。
        /// </summary>
        /// <param name="profile">対象プロファイル。</param>
        /// <param name="bindings">起動対象の binding 一覧（null 要素・無効 binding は無視する）。null 可。</param>
        /// <param name="warnings">補えなかった理由を追加する一覧。null なら警告を集めない。</param>
        /// <returns>
        /// 外側のインデックスが <see cref="FacialProfile.Layers"/> と揃った宣言配列。長さは
        /// <see cref="FacialProfile.Layers"/> と <see cref="FacialProfile.LayerInputSources"/> の長い方。
        /// 補っていないレイヤーの内側配列はプロファイルのものをそのまま参照する。
        /// </returns>
        public static InputSourceDeclaration[][] Resolve(
            FacialProfile profile,
            IReadOnlyList<AdapterBindingBase> bindings,
            List<string> warnings)
        {
            ReadOnlySpan<LayerDefinition> layers = profile.Layers.Span;
            ReadOnlySpan<InputSourceDeclaration[]> declared = profile.LayerInputSources.Span;
            int layerCount = layers.Length;

            var result = new InputSourceDeclaration[Math.Max(layerCount, declared.Length)][];
            for (int i = 0; i < result.Length; i++)
            {
                result[i] = i < declared.Length && declared[i] != null
                    ? declared[i]
                    : Array.Empty<InputSourceDeclaration>();
            }

            if (!HasTargetLayerBinding(bindings))
            {
                return result;
            }

            var layerNames = new string[layerCount];
            var declaredIds = new HashSet<string>(StringComparer.Ordinal);
            for (int l = 0; l < layerCount; l++)
            {
                layerNames[l] = layers[l].Name;
                InputSourceDeclaration[] layerDeclarations = result[l];
                for (int d = 0; d < layerDeclarations.Length; d++)
                {
                    declaredIds.Add(layerDeclarations[d].Id);
                }
            }

            List<TargetLayerInputAssignment> assignments =
                ResolveCore(layerNames, declaredIds, bindings, useConfiguredId: false, warnings);
            for (int i = 0; i < assignments.Count; i++)
            {
                TargetLayerInputAssignment assignment = assignments[i];
                result[assignment.LayerIndex] = Append(
                    result[assignment.LayerIndex],
                    new InputSourceDeclaration(assignment.InputSourceId, AutoDeclarationWeight, null));
            }

            return result;
        }

        /// <summary>
        /// 起動前（Editor のルーティング表示など）に、binding の入力源がどのレイヤーへ自動で補われるかを求める。
        /// </summary>
        /// <remarks>
        /// 規則は <see cref="Resolve"/> と同じ。入力源 id には起動状態に依存しない
        /// <see cref="IAdapterBindingTargetLayerInput.ConfiguredTargetLayerInputSourceId"/> を使う。
        /// </remarks>
        /// <param name="layerNames">プロファイルのレイヤー名（宣言順）。null 要素は空名として扱う。</param>
        /// <param name="declaredInputSourceIds">いずれかのレイヤーに手動で宣言済みの入力源 id。null 可。</param>
        /// <param name="bindings">binding 一覧（null 要素・無効 binding は無視する）。null 可。</param>
        /// <param name="warnings">補えなかった理由を追加する一覧。null なら警告を集めない。</param>
        /// <returns>補う宣言の一覧（binding の並び順）。</returns>
        public static IReadOnlyList<TargetLayerInputAssignment> ResolveConfigured(
            IReadOnlyList<string> layerNames,
            IEnumerable<string> declaredInputSourceIds,
            IReadOnlyList<AdapterBindingBase> bindings,
            List<string> warnings)
        {
            if (bindings == null)
            {
                return Array.Empty<TargetLayerInputAssignment>();
            }

            var declaredIds = declaredInputSourceIds != null
                ? new HashSet<string>(declaredInputSourceIds, StringComparer.Ordinal)
                : new HashSet<string>(StringComparer.Ordinal);
            return ResolveCore(
                layerNames ?? Array.Empty<string>(),
                declaredIds,
                bindings,
                useConfiguredId: true,
                warnings);
        }

        /// <summary>
        /// 補う規則の本体。<paramref name="declaredIds"/> には補った id を追加する（同じ id を 2 回補わない）。
        /// </summary>
        private static List<TargetLayerInputAssignment> ResolveCore(
            IReadOnlyList<string> layerNames,
            HashSet<string> declaredIds,
            IReadOnlyList<AdapterBindingBase> bindings,
            bool useConfiguredId,
            List<string> warnings)
        {
            var assignments = new List<TargetLayerInputAssignment>();
            int layerCount = layerNames.Count;

            for (int b = 0; b < bindings.Count; b++)
            {
                AdapterBindingBase binding = bindings[b];
                if (binding == null || binding.Disabled || !(binding is IAdapterBindingTargetLayerInput target))
                {
                    continue;
                }

                string id = useConfiguredId ? target.ConfiguredTargetLayerInputSourceId : target.TargetLayerInputSourceId;
                if (string.IsNullOrWhiteSpace(id) || declaredIds.Contains(id))
                {
                    continue;
                }

                if (layerCount == 0)
                {
                    warnings?.Add(
                        $"入力源 '{id}' を足すレイヤーがプロファイルに 1 つもないため、自動で宣言しません。");
                    continue;
                }

                string layerName = target.TargetLayerName;
                int layerIndex = string.IsNullOrWhiteSpace(layerName) ? 0 : IndexOfLayer(layerNames, layerName);
                if (layerIndex < 0)
                {
                    warnings?.Add(
                        $"入力源 '{id}' の対象レイヤー '{layerName}' がプロファイルにないため、自動で宣言しません。"
                        + " binding の対象レイヤーを既存のレイヤーから選び直してください。");
                    continue;
                }

                declaredIds.Add(id);
                assignments.Add(new TargetLayerInputAssignment(layerIndex, id));
            }

            return assignments;
        }

        private static bool HasTargetLayerBinding(IReadOnlyList<AdapterBindingBase> bindings)
        {
            if (bindings == null)
            {
                return false;
            }

            for (int b = 0; b < bindings.Count; b++)
            {
                if (bindings[b] is IAdapterBindingTargetLayerInput && !bindings[b].Disabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static int IndexOfLayer(IReadOnlyList<string> layerNames, string name)
        {
            for (int i = 0; i < layerNames.Count; i++)
            {
                if (string.Equals(layerNames[i], name, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private static InputSourceDeclaration[] Append(InputSourceDeclaration[] source, InputSourceDeclaration item)
        {
            var appended = new InputSourceDeclaration[source.Length + 1];
            Array.Copy(source, appended, source.Length);
            appended[source.Length] = item;
            return appended;
        }
    }

    /// <summary>
    /// <see cref="TargetLayerInputSourceResolver"/> が自動で補う 1 件の宣言（どのレイヤーへどの入力源 id を足すか）。
    /// </summary>
    public readonly struct TargetLayerInputAssignment
    {
        public TargetLayerInputAssignment(int layerIndex, string inputSourceId)
        {
            LayerIndex = layerIndex;
            InputSourceId = inputSourceId ?? string.Empty;
        }

        /// <summary>補う先のレイヤーのインデックス。</summary>
        public int LayerIndex { get; }

        /// <summary>補う入力源 id。</summary>
        public string InputSourceId { get; }
    }
}
