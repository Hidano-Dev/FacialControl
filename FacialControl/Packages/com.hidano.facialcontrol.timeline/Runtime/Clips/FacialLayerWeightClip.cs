using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Clips
{
    /// <summary>
    /// レイヤー weight（0..1）のカーブ。時刻は Clip の開始からの相対秒。
    /// </summary>
    [Serializable]
    public sealed class FacialLayerWeightClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField] private AnimationCurve weight = AnimationCurve.Constant(0f, 1f, 1f);

        /// <summary>レイヤー weight のカーブ（範囲外の値は注入時に 0..1 へクランプされる）。</summary>
        public AnimationCurve Weight
        {
            get => weight;
            set => weight = value ?? new AnimationCurve();
        }

        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            // 値は Track の Mixer が Clip 一覧から直接評価する（FacialValueClip と同じ）。
            return Playable.Create(graph);
        }
    }
}
