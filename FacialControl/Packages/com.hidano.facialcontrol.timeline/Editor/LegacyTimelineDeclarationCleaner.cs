using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Domain.Services;
using UnityEditor;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// Profile SO の <c>Layers[].inputSources</c> に残る Timeline の宣言 1 件。
    /// </summary>
    public readonly struct LegacyTimelineDeclaration
    {
        public LegacyTimelineDeclaration(string layerName, string declaredId, bool isStateDeclaration)
        {
            LayerName = layerName ?? string.Empty;
            DeclaredId = declaredId ?? string.Empty;
            IsStateDeclaration = isStateDeclaration;
        }

        public string LayerName { get; }

        public string DeclaredId { get; }

        /// <summary>true: 旧 <c>{slug}:*:state</c> 宣言（削除対象）。false: 値 sink 宣言（宣言 weight が優先される。Info のみ）。</summary>
        public bool IsStateDeclaration { get; }
    }

    /// <summary>
    /// Profile SO の旧 <c>{slug}:*:state</c> 宣言を見つけ、Undo 可能に削除する（D2）。値 sink 宣言は列挙するだけで変更しない。
    /// </summary>
    /// <remarks>
    /// 判定は Connector の Play 検出と同じ <see cref="TimelineSinkIdConvention.IsLegacyStateDeclaration"/> を使う（Edit と Play で結果がずれない）。
    /// </remarks>
    public static class LegacyTimelineDeclarationCleaner
    {
        private const string LayersPropertyPath = "_layers";
        private const string InputSourcesPropertyName = "inputSources";
        private const string IdPropertyName = "id";
        private const string UndoName = "Remove legacy timeline state declarations";

        /// <summary>Profile 内の Timeline 宣言（state 宣言と値 sink 宣言）を列挙する。SO は変更しない。</summary>
        public static IReadOnlyList<LegacyTimelineDeclaration> Scan(FacialCharacterProfileSO profileAsset, AdapterSlug slug)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            var result = new List<LegacyTimelineDeclaration>();
            if (slug.Value == null || profileAsset.Layers == null)
            {
                return result;
            }

            string prefix = slug.Value + ":";
            List<LayerDefinitionSerializable> layers = profileAsset.Layers;
            for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
            {
                LayerDefinitionSerializable layer = layers[layerIndex];
                if (layer?.inputSources == null)
                {
                    continue;
                }

                for (int i = 0; i < layer.inputSources.Count; i++)
                {
                    string id = layer.inputSources[i]?.id;
                    if (string.IsNullOrEmpty(id) || !id.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    result.Add(new LegacyTimelineDeclaration(
                        layer.name,
                        id,
                        TimelineSinkIdConvention.IsLegacyStateDeclaration(id, slug)));
                }
            }

            return result;
        }

        /// <summary>
        /// 旧 state 宣言だけを削除して件数を返す。Undo.RecordObject → SerializedObject 経由で削除 → ApplyModifiedProperties + SetDirty →
        /// <see cref="TimelineProfileSource"/> のキャッシュ無効化。値 sink 宣言とその weight は変更しない。削除対象が無ければ何もしない。
        /// </summary>
        public static int RemoveStateDeclarations(FacialCharacterProfileSO profileAsset, AdapterSlug slug)
        {
            if (profileAsset == null)
            {
                throw new ArgumentNullException(nameof(profileAsset));
            }

            if (slug.Value == null || !HasStateDeclaration(Scan(profileAsset, slug)))
            {
                return 0;
            }

            Undo.RecordObject(profileAsset, UndoName);
            var serialized = new SerializedObject(profileAsset);
            SerializedProperty layers = serialized.FindProperty(LayersPropertyPath);
            int removed = 0;
            if (layers != null && layers.isArray)
            {
                for (int layerIndex = 0; layerIndex < layers.arraySize; layerIndex++)
                {
                    SerializedProperty inputSources = layers.GetArrayElementAtIndex(layerIndex).FindPropertyRelative(InputSourcesPropertyName);
                    if (inputSources == null || !inputSources.isArray)
                    {
                        continue;
                    }

                    // 後ろから消すと index がずれない。
                    for (int i = inputSources.arraySize - 1; i >= 0; i--)
                    {
                        SerializedProperty id = inputSources.GetArrayElementAtIndex(i).FindPropertyRelative(IdPropertyName);
                        if (id != null && TimelineSinkIdConvention.IsLegacyStateDeclaration(id.stringValue, slug))
                        {
                            inputSources.DeleteArrayElementAtIndex(i);
                            removed++;
                        }
                    }
                }
            }

            if (removed > 0)
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(profileAsset);
                TimelineProfileSource.InvalidateCache(profileAsset);
            }

            return removed;
        }

        /// <summary>Profile の Timeline binding の slug（無い・空・不正なら既定 <c>timeline</c>）。</summary>
        public static AdapterSlug ResolveSlug(FacialCharacterProfileSO profileAsset)
        {
            IReadOnlyList<AdapterBindingBase> bindings = profileAsset != null ? profileAsset.AdapterBindings : null;
            if (bindings != null)
            {
                for (int i = 0; i < bindings.Count; i++)
                {
                    if (bindings[i] is TimelineAdapterBinding binding
                        && !string.IsNullOrWhiteSpace(binding.Slug)
                        && AdapterSlug.TryParse(binding.Slug, out AdapterSlug slug))
                    {
                        return slug;
                    }
                }
            }

            return AdapterSlug.Parse(TimelineSinkIdConvention.DefaultSlug);
        }

        private static bool HasStateDeclaration(IReadOnlyList<LegacyTimelineDeclaration> found)
        {
            for (int i = 0; i < found.Count; i++)
            {
                if (found[i].IsStateDeclaration)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
