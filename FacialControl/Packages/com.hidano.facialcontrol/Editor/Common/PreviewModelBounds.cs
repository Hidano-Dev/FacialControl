using UnityEngine;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// プレビュー用モデルの構図計算に使う bounds。
    /// Expression Creator のプレビューと Inspector の Expression サムネイルで共用する。
    /// </summary>
    public static class PreviewModelBounds
    {
        /// <summary>
        /// 配下の全 Renderer（非アクティブを含む）の bounds を合成して返す。
        /// Renderer が 1 つも無い場合はルート位置を中心とする 1m 立方体を返す。
        /// </summary>
        public static Bounds Calculate(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            return bounds;
        }
    }
}
