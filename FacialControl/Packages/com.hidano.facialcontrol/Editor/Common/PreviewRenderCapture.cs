using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Hidano.FacialControl.Editor.Common
{
    /// <summary>
    /// <see cref="PreviewRenderUtility"/> のカメラで 1 枚オフスクリーン描画し、
    /// <see cref="Texture2D"/> として読み出すユーティリティ。
    /// <para>
    /// Expression Creator のプレビュー書き出しと、Inspector の Expression サムネイル生成で共用する。
    /// カメラの位置・向きは呼び出し側で設定済みであること。
    /// </para>
    /// </summary>
    public static class PreviewRenderCapture
    {
        /// <summary>
        /// 現在のカメラ設定で描画し、<paramref name="width"/> x <paramref name="height"/> の
        /// <see cref="Texture2D"/>（RGBA32 / mip なし / CPU 読み出し可）を返す。
        /// Built-in RP で描画結果が取得できない場合は null。
        /// 返したテクスチャの破棄は呼び出し側の責務。
        /// </summary>
        public static Texture2D Capture(PreviewRenderUtility previewRenderUtility, int width, int height)
        {
            if (previewRenderUtility == null)
                throw new ArgumentNullException(nameof(previewRenderUtility));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width), width, "Width must be greater than zero.");
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height), height, "Height must be greater than zero.");

            var rect = new Rect(0f, 0f, width, height);
            var previousActive = RenderTexture.active;

            // SRP(URP) では GUI コンテキスト外（ボタンクリック等）からの PreviewRenderUtility.Render()
            // （camera.Render() 経由）が何も描画せず、EndPreview() は直前に画面へ描画された内容が
            // 残った RenderTexture を返す。このため明示的な RenderRequest でオフスクリーン描画する。
            var request = new RenderPipeline.StandardRequest();
            if (RenderPipeline.SupportsRenderRequest(previewRenderUtility.camera, request))
            {
                // MSAA 付き一時 RT は URP の最終 depth copy で resolve surface エラーになるため使わない
                var renderTexture = RenderTexture.GetTemporary(
                    width, height, 24, RenderTextureFormat.ARGB32);
                try
                {
                    // BeginPreview / EndPreview でプレビューシーンのライティング設定を
                    // on-screen 描画（Render(rect)）と揃える。
                    previewRenderUtility.BeginPreview(rect, GUIStyle.none);
                    try
                    {
                        request.destination = renderTexture;
                        RenderPipeline.SubmitRenderRequest(previewRenderUtility.camera, request);
                    }
                    finally
                    {
                        previewRenderUtility.EndPreview();
                    }

                    return ReadPixels(renderTexture, rect);
                }
                finally
                {
                    RenderTexture.active = previousActive;
                    RenderTexture.ReleaseTemporary(renderTexture);
                }
            }

            // Built-in RP fallback: PreviewRenderUtility の描画結果を読み取る
            previewRenderUtility.BeginPreview(rect, GUIStyle.none);
            try
            {
                previewRenderUtility.Render(true, true);
                var renderTexture = previewRenderUtility.EndPreview() as RenderTexture;
                if (renderTexture == null)
                    return null;

                return ReadPixels(renderTexture, rect);
            }
            finally
            {
                RenderTexture.active = previousActive;
            }
        }

        private static Texture2D ReadPixels(RenderTexture source, Rect rect)
        {
            RenderTexture.active = source;
            var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGBA32, false);
            texture.ReadPixels(rect, 0, 0);
            texture.Apply();
            return texture;
        }
    }
}
