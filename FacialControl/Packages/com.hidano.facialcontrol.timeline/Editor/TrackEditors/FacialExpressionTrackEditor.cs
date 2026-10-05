using Hidano.FacialControl.Timeline.Editor.Validation;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.TrackEditors
{
    [CustomTimelineEditor(typeof(FacialExpressionTrack))]
    public sealed class FacialExpressionTrackEditor : TrackEditor
    {
        public override TrackDrawOptions GetTrackOptions(TrackAsset track, Object binding)
        {
            return BuildTrackOptions(track, base.GetTrackOptions(track, binding), FacialTimelineValidator.Validate(track));
        }

        /// <summary>トラック（とその Clip）の変更。Watcher へ ClipEdit で流す（購読は持たない）。</summary>
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

        public static TrackDrawOptions BuildTrackOptions(
            TrackAsset track,
            TrackDrawOptions options,
            FacialTimelineValidationReport report)
        {
            if (track == null || report == null)
            {
                return options;
            }

            string message = report.GetTrackMessage(track);
            if (!string.IsNullOrEmpty(message))
            {
                options.errorText = message;
            }

            return options;
        }
    }
}
