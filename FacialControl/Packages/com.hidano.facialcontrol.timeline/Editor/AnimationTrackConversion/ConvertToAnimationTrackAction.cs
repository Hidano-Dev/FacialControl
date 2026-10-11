using System.Collections.Generic;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// Timeline ウィンドウのトラック右クリックメニュー「FacialControl/AnimationTrack へ変換」（HID-190）。
    /// ダイアログは出さず、選択した独自 Track の対象ごとに直ちに変換する（<see cref="FacialAnimationTrackConverter"/>）。
    /// </summary>
    [MenuEntry("FacialControl/AnimationTrack へ変換")]
    public sealed class ConvertToAnimationTrackAction : TrackAction
    {
        public override ActionValidity Validate(IEnumerable<TrackAsset> tracks)
        {
            if (TimelineEditor.inspectedDirector == null || UnityEngine.Application.isPlaying)
            {
                return ActionValidity.NotApplicable;
            }

            foreach (TrackAsset track in tracks)
            {
                if (FacialAnimationTrackConverter.IsFacialTrack(track))
                {
                    return ActionValidity.Valid;
                }
            }

            return ActionValidity.NotApplicable;
        }

        public override bool Execute(IEnumerable<TrackAsset> tracks)
        {
            PlayableDirector director = TimelineEditor.inspectedDirector;
            if (director == null)
            {
                return false;
            }

            List<AnimationTrackConversionResult> results = FacialAnimationTrackConverter.Convert(director, tracks);
            if (results.Count == 0)
            {
                return false;
            }

            TimelineEditor.Refresh(RefreshReason.ContentsAddedOrRemoved);
            return true;
        }
    }
}
