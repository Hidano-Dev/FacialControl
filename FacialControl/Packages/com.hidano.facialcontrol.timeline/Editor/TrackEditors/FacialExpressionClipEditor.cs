using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor.Validation;
using UnityEditor.Timeline;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.TrackEditors
{
    [CustomTimelineEditor(typeof(FacialExpressionClip))]
    public sealed class FacialExpressionClipEditor : ClipEditor
    {
        public override ClipDrawOptions GetClipOptions(TimelineClip clip)
        {
            return BuildClipOptions(clip, base.GetClipOptions(clip), FacialTimelineValidator.Validate(clip.GetParentTrack()));
        }

        /// <summary>移動・トリム・Inspector 編集。Watcher へ ClipEdit で流す（購読は持たない）。</summary>
        public override void OnClipChanged(TimelineClip clip)
        {
            base.OnClipChanged(clip);
            TimelineEditNotifier.NotifyClipEdit(clip);
        }

        /// <summary>追加・複製。Watcher へ ClipEdit で流す。</summary>
        public override void OnCreate(TimelineClip clip, TrackAsset track, TimelineClip clonedFrom)
        {
            base.OnCreate(clip, track, clonedFrom);
            TimelineEditNotifier.NotifyClipEdit(track != null ? track.timelineAsset : null);
        }

        public static ClipDrawOptions BuildClipOptions(
            TimelineClip clip,
            ClipDrawOptions options,
            FacialTimelineValidationReport report)
        {
            if (clip == null || report == null)
            {
                return options;
            }

            string message = report.GetClipMessage(clip);
            if (!string.IsNullOrEmpty(message))
            {
                options.errorText = message;
                options.tooltip = message;
            }

            return options;
        }
    }
}
