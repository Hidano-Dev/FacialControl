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

            if (bindings == null)
            {
                return result;
            }

            for (int b = 0; b < bindings.Count; b++)
            {
                AdapterBindingBase binding = bindings[b];
                if (binding == null || binding.Disabled || !(binding is IAdapterBindingTargetLayerInput target))
                {
                    continue;
                }

                string id = target.TargetLayerInputSourceId;
                if (string.IsNullOrWhiteSpace(id) || IsDeclared(result, layerCount, id))
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
                int layerIndex = string.IsNullOrWhiteSpace(layerName) ? 0 : IndexOfLayer(layers, layerName);
                if (layerIndex < 0)
                {
                    warnings?.Add(
                        $"入力源 '{id}' の対象レイヤー '{layerName}' がプロファイルにないため、自動で宣言しません。"
                        + " binding の対象レイヤーを既存のレイヤーから選び直してください。");
                    continue;
                }

                result[layerIndex] = Append(
                    result[layerIndex],
                    new InputSourceDeclaration(id, AutoDeclarationWeight, null));
            }

            return result;
        }

        private static bool IsDeclared(InputSourceDeclaration[][] declarations, int layerCount, string id)
        {
            int upper = Math.Min(layerCount, declarations.Length);
            for (int l = 0; l < upper; l++)
            {
                InputSourceDeclaration[] layerDeclarations = declarations[l];
                for (int d = 0; d < layerDeclarations.Length; d++)
                {
                    if (string.Equals(layerDeclarations[d].Id, id, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int IndexOfLayer(ReadOnlySpan<LayerDefinition> layers, string name)
        {
            for (int i = 0; i < layers.Length; i++)
            {
                if (string.Equals(layers[i].Name, name, StringComparison.Ordinal))
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
}
