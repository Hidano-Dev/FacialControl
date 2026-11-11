using System.Collections.Generic;
using UnityEditor.Timeline;
using UnityEditor.Timeline.Actions;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// Timeline ウィンドウのトラック右クリックメニュー「FacialControl/AnimationTrack へ変換」（HID-190）。
    /// Clip の分け方とフレームレートを選ぶダイアログ（<see cref="ConvertToAnimationTrackDialog"/>。HID-422）を出し、
    /// 選択した独自 Track の対象ごとに変換する（<see cref="FacialAnimationTrackConverter"/>）。
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
                if (FacialAnimationTrackConverter.IsFacialTrack(track) || track is Tracks.FacialCharacterTrack)
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

            // Clip の分け方とフレームレートを選んでから変換する（HID-422。変換と Refresh はダイアログが行う）。
            ConvertToAnimationTrackDialog.Open(director, tracks);
            return true;
        }
    }
}
