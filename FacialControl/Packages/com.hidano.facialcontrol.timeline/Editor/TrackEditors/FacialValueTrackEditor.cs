using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.TrackEditors
{
    /// <summary>
    /// Value トラックの Timeline ウィンドウ操作を変更通知として Watcher へ流す（D8 経路 (a)）。購読は持たない。
    /// </summary>
    [CustomTimelineEditor(typeof(FacialValueTrack))]
    public sealed class FacialValueTrackEditor : TrackEditor
    {
        /// <summary>トラック（とその Clip）の変更。Watcher へ ClipEdit で流す。</summary>
        public override void OnTrackChanged(TrackAsset track)
        {
            base.OnTrackChanged(track);
            TimelineEditNotifier.NotifyClipEdit(track != null ? track.timelineAsset : null);
        }

        /// <summary>作成・複製。兄弟トラックの Bake 参照を補完してから Watcher へ流す。</summary>
        public override void OnCreate(TrackAsset track, TrackAsset copiedFrom)
        {
            base.OnCreate(track, copiedFrom);
            TimelineEditNotifier.OnTrackCreated(track);
        }
    }
}
