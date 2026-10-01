using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Inspector.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsBase"/> 派生の具象 ScriptableObject 型を
    /// <see cref="TypeCache"/> 経由で列挙する Editor 専用 helper。
    /// </summary>
    /// <remarks>
    /// task 6.3 / 要件 6.2 に対応。<see cref="AdapterRuntimeSettingsCollectionEditor"/>
    /// (task 6.4) の Add ボタンが表示する型一覧の供給元。
    /// abstract / generic / interface と、<see cref="HideInAdapterRuntimeSettingsMenuAttribute"/>
    /// を付けた型は列挙対象から除外する。
    /// </remarks>
    public static class AdapterRuntimeSettingsTypeRegistry
    {
        /// <summary>
        /// <see cref="AdapterRuntimeSettingsBase"/> 派生の具象型を displayName 昇順
        /// (<see cref="StringComparer.OrdinalIgnoreCase"/>) で返す。
        /// </summary>
        public static IReadOnlyList<Type> GetConcreteTypes()
        {
            var derived = TypeCache.GetTypesDerivedFrom<AdapterRuntimeSettingsBase>();
            var collected = new List<Type>(derived.Count);

            foreach (var type in derived)
            {
                if (type == null)
                {
                    continue;
                }

                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition)
                {
                    continue;
                }

                // 移行用に残した旧型は新規追加させない。
                if (type.IsDefined(typeof(HideInAdapterRuntimeSettingsMenuAttribute), inherit: false))
                {
                    continue;
                }

                collected.Add(type);
            }

            collected.Sort((a, b) =>
            {
                int cmp = string.Compare(GetDisplayName(a), GetDisplayName(b), StringComparison.OrdinalIgnoreCase);
                if (cmp != 0)
                {
                    return cmp;
                }

                return string.Compare(a.FullName, b.FullName, StringComparison.Ordinal);
            });

            return collected;
        }

        /// <summary>
        /// 指定型の UI 表示用 displayName を返す。<c>[CreateAssetMenu]</c> の <c>menuName</c>
        /// が指定されていればそれを、未指定なら型名 (<see cref="Type.Name"/>) を返す。
        /// </summary>
        public static string GetDisplayName(Type type)
        {
            if (type == null)
            {
                return string.Empty;
            }

            var attribute = type.GetCustomAttribute<CreateAssetMenuAttribute>(inherit: false);
            if (attribute != null && !string.IsNullOrWhiteSpace(attribute.menuName))
            {
                return attribute.menuName;
            }

            return type.Name;
        }
    }
}
