using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Hidano.FacialControl.ExpressionCreator
{
    /// <summary>
    /// 参照モデルから bone 名候補を列挙するユーティリティ。
    /// Editor 拡張の bone 名入力におけるタイポ防止を目的とし、
    /// FacialProfileSO_BonePoseView のドロップダウン候補生成に使用する。
    /// </summary>
    /// <remarks>
    /// - 参照モデル配下の全 Transform から名前を再帰収集する
    /// - 重複排除およびアルファベット順ソート済みの配列を返す
    /// - null 参照モデル / null Transform / 名前空文字はスキップ
    /// - 候補が 0 件の場合は空配列（<see cref="Array.Empty{T}"/>）を返す
    /// </remarks>
    public static class BoneNameProvider
    {
        /// <summary>
        /// 参照モデル（GameObject）配下の全 Transform から bone 名を列挙する。
        /// </summary>
        /// <param name="referenceModel">bone 名を取得するモデルルート。null の場合は空配列を返す</param>
        /// <returns>重複排除・アルファベット順ソート済みの bone 名配列</returns>
        public static string[] GetBoneNames(GameObject referenceModel)
        {
            if (referenceModel == null)
                return Array.Empty<string>();

            var transforms = referenceModel.GetComponentsInChildren<Transform>(includeInactive: true);
            return GetBoneNames(transforms);
        }

        /// <summary>
        /// Transform 配列から bone 名を列挙する。
        /// </summary>
        /// <param name="transforms">対象の Transform 配列。null 要素および name 空文字の要素はスキップ</param>
        /// <returns>重複排除・アルファベット順ソート済みの bone 名配列</returns>
        public static string[] GetBoneNames(Transform[] transforms)
        {
            if (transforms == null || transforms.Length == 0)
                return Array.Empty<string>();

            var nameSet = new HashSet<string>();
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null)
                    continue;

                var boneName = t.name;
                if (!string.IsNullOrEmpty(boneName))
                {
                    nameSet.Add(boneName);
                }
            }

            if (nameSet.Count == 0)
                return Array.Empty<string>();

            return nameSet.OrderBy(n => n, StringComparer.Ordinal).ToArray();
        }
    }
}
