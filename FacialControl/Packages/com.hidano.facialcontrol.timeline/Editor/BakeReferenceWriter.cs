using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using UnityEditor;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// TimelineAsset 内の全 Facial トラック（root + 子）へ同一の Bake サブアセット参照を書く（D5）。
    /// Runtime の <see cref="FacialTimelineBakeLocator"/> が「全トラックが同一参照」のときだけ Found を返すため、
    /// Export / 再ベイクの後処理として必ず本クラスを通す。
    /// </summary>
    /// <remarks>
    /// Bake とトラックの参照は内部キャッシュなので Undo スタックには載せず <see cref="EditorUtility.SetDirty"/> だけ行う。
    /// Bake には <see cref="HideFlags.HideInHierarchy"/> を付け、Project ウィンドウに出さない。
    /// </remarks>
    public static class BakeReferenceWriter
    {
        /// <summary>全 Facial トラックへ <paramref name="bake"/> を書く。いずれかの参照が変わったら true。</summary>
        public static bool Apply(TimelineAsset timeline, FacialTimelineBakeAsset bake)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            if (bake == null)
            {
                throw new ArgumentNullException(nameof(bake));
            }

            if ((bake.hideFlags & HideFlags.HideInHierarchy) == 0)
            {
                bake.hideFlags |= HideFlags.HideInHierarchy;
                EditorUtility.SetDirty(bake);
            }

            bool changed = false;
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(timeline).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (!(tracks[i] is IFacialTimelineBakeHolder holder) || ReferenceEquals(holder.Bake, bake))
                {
                    continue;
                }

                holder.Bake = bake;
                EditorUtility.SetDirty(tracks[i]);
                changed = true;
            }

            if (changed)
            {
                EditorUtility.SetDirty(timeline);
            }

            return changed;
        }
    }
}
