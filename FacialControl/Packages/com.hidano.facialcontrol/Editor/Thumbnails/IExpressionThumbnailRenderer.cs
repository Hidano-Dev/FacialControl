using System;
using Hidano.FacialControl.Domain.Models;
using UnityEngine;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// 参照モデルに Expression を適用した顔を 1 枚描画する。
    /// <see cref="ExpressionThumbnailService"/> から GPU 描画の境界として差し替えられる。
    /// </summary>
    public interface IExpressionThumbnailRenderer : IDisposable
    {
        /// <summary>
        /// <paramref name="referenceModel"/> に <paramref name="clip"/> / <paramref name="snapshot"/> を適用して描画し、
        /// <paramref name="resolution"/> x <paramref name="resolution"/> の CPU 読み出し可能なテクスチャを返す。
        /// 描画できない環境（グラフィックスデバイスなし等）では null。返したテクスチャの破棄は呼び出し側の責務。
        /// </summary>
        Texture2D Render(GameObject referenceModel, AnimationClip clip, in ExpressionSnapshot snapshot, int resolution);
    }
}
