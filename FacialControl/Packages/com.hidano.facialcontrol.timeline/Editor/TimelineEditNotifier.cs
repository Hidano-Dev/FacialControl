using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using UnityEditor;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor
{
    /// <summary>
    /// TrackEditor / ClipEditor の override（Timeline ウィンドウでの移動・トリム・追加）から呼ばれ、
    /// <see cref="TimelineEditorServices.ChangeWatcher"/> へ <see cref="TimelineDirtyReason.ClipEdit"/> で MarkDirty する（D8 経路 (a)）。
    /// Editor 側は Unity イベントを購読しない。
    /// </summary>
    internal static class TimelineEditNotifier
    {
        /// <summary>Clip / Track の変更を Watcher へ流す。Services が停止中なら <see cref="MarkDirtyResult.Ignored"/>。</summary>
        public static MarkDirtyResult NotifyClipEdit(TimelineAsset timeline)
        {
            TimelineEditChangeWatcher watcher = TimelineEditorServices.ChangeWatcher;
            if (watcher == null || timeline == null)
            {
                return MarkDirtyResult.Ignored;
            }

            return watcher.MarkDirty(timeline, TimelineDirtyReason.ClipEdit);
        }

        /// <summary>Clip の親トラックが属する Timeline へ変更を流す。</summary>
        public static MarkDirtyResult NotifyClipEdit(TimelineClip clip)
        {
            TrackAsset track = clip?.GetParentTrack();
            return NotifyClipEdit(track != null ? track.timelineAsset : null);
        }

        /// <summary>
        /// 作成された（またはコピーされた）トラックの Bake 参照を、同じ Timeline の兄弟 Facial トラックが共有する参照で補完する。
        /// 兄弟が 1 種類の非 null 参照で一致しているときだけ書く（割れている・無いときは走査順で選ばず、再ベイクの修復に任せる）。
        /// Bake 参照は内部キャッシュなので Undo には載せず SetDirty のみ（D8）。参照が変わったら true。
        /// </summary>
        public static bool CompleteBakeReferenceFromSiblings(TrackAsset track)
        {
            if (!(track is IFacialTimelineBakeHolder holder) || track.timelineAsset == null)
            {
                return false;
            }

            FacialTimelineBakeAsset shared = null;
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(track.timelineAsset).TrackAssets;
            for (int i = 0; i < tracks.Count; i++)
            {
                if (ReferenceEquals(tracks[i], track) || !(tracks[i] is IFacialTimelineBakeHolder sibling) || sibling.Bake == null)
                {
                    continue;
                }

                if (shared == null)
                {
                    shared = sibling.Bake;
                }
                else if (!ReferenceEquals(shared, sibling.Bake))
                {
                    return false;
                }
            }

            if (shared == null || ReferenceEquals(holder.Bake, shared))
            {
                return false;
            }

            holder.Bake = shared;
            EditorUtility.SetDirty(track);
            return true;
        }

        /// <summary>トラック作成時の共通処理: 兄弟の Bake 参照を補完してから変更を流す。</summary>
        public static void OnTrackCreated(TrackAsset track)
        {
            if (track == null)
            {
                return;
            }

            CompleteBakeReferenceFromSiblings(track);
            NotifyClipEdit(track.timelineAsset);
        }
    }
}
