using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Thumbnails;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// GPU 描画の代わりに、指定解像度の空テクスチャを返す <see cref="IExpressionThumbnailRenderer"/>。
    /// 呼ばれた回数と、返したテクスチャが破棄されたかを記録する。
    /// </summary>
    internal sealed class FakeExpressionThumbnailRenderer : IExpressionThumbnailRenderer
    {
        private readonly List<Texture2D> _rendered = new List<Texture2D>();

        public int RenderCount { get; private set; }
        public int LastResolution { get; private set; }
        public bool ReturnNull { get; set; }
        public bool Disposed { get; private set; }

        /// <summary>このフェイクが返したテクスチャのうち、まだ破棄されていない数。</summary>
        public int LiveTextureCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _rendered.Count; i++)
                {
                    if (_rendered[i] != null) count++;
                }
                return count;
            }
        }

        public Texture2D Render(GameObject referenceModel, AnimationClip clip, in ExpressionSnapshot snapshot, int resolution)
        {
            RenderCount++;
            LastResolution = resolution;
            if (ReturnNull) return null;

            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
            _rendered.Add(texture);
            return texture;
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
