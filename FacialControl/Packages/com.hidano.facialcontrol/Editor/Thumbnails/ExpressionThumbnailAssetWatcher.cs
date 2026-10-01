using System;
using UnityEditor;

namespace Hidano.FacialControl.Editor.Thumbnails
{
    /// <summary>
    /// アセットのインポート（保存・再インポート・外部からの変更）を <see cref="ExpressionThumbnailService"/> へ知らせる。
    /// 参照モデルの FBX / prefab、そのマテリアル・テクスチャ、AnimationClip のディスク上の変更は
    /// dirty count に現れないことがあるため、インポートイベントで拾う。
    /// </summary>
    internal sealed class ExpressionThumbnailAssetWatcher : AssetPostprocessor
    {
        /// <summary>インポート・移動されたアセットのパス一覧。</summary>
        public static event Action<string[]> AssetsImported;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            var handler = AssetsImported;
            if (handler == null) return;

            if (importedAssets.Length > 0)
            {
                handler(importedAssets);
            }
            if (movedAssets.Length > 0)
            {
                handler(movedAssets);
            }
        }
    }
}
