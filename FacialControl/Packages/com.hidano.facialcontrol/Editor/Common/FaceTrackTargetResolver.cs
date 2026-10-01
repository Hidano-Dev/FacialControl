using System;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// プレビューカメラが注視する「顔」ジョイントをモデル階層から自動解決するユーティリティ。
    /// <para>
    /// 解決順序:
    /// 1. 階層内の Humanoid Animator（<see cref="Animator.isHuman"/>）の Head ボーン
    /// 2. Generic モデル向けフォールバック: 名前に "head" を含むジョイント
    /// 3. 名前に "neck" を含むジョイント
    /// </para>
    /// <remarks>
    /// 名前検索はジョイント（ボーン Transform）を対象とするため、Renderer を持つ
    /// GameObject（"Head" 等の名前を持つメッシュオブジェクト）はスキップする。
    /// メッシュオブジェクトの Transform は原点に置かれることが多く、注視点として不適切なため。
    /// ルート自身も除外する（全身中心と等価でトラッキング対象として意味を持たないため）。
    /// 特殊なモデルでは誤検出しうるので、呼び出し側 UI で手動修正できるようにすること。
    /// </remarks>
    /// </summary>
    public static class FaceTrackTargetResolver
    {
        /// <summary>
        /// モデル階層からトラッキング対象 Transform を解決する。見つからない場合は null。
        /// </summary>
        /// <param name="root">モデルルート。null の場合は null を返す</param>
        public static Transform Resolve(GameObject root)
        {
            if (root == null)
                return null;

            var animators = root.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; i++)
            {
                var animator = animators[i];
                if (animator == null || !animator.isHuman)
                    continue;

                var head = animator.GetBoneTransform(HumanBodyBones.Head);
                if (head != null)
                    return head;
            }

            return FindJointByNameContains(root.transform, "head")
                ?? FindJointByNameContains(root.transform, "neck");
        }

        private static Transform FindJointByNameContains(Transform root, string keyword)
        {
            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == root)
                    continue;

                if (t.GetComponent<Renderer>() != null)
                    continue;

                if (t.name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
                    return t;
            }

            return null;
        }
    }
}
