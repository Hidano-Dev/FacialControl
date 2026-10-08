using System;
using System.Collections.Generic;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.EditorPreview;
using Hidano.FacialControl.Timeline.Playables;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    /// <summary>
    /// レイヤー weight（<c>FacialController.SetLayerWeight</c> と同じ inter-layer weight）を駆動するトラック。
    /// 再生中は live のレイヤー weight 書き込み（発話ゲート等）を止め、REC の weight 記録を再現する。
    /// </summary>
    /// <remarks>
    /// Bake を持たない（レイヤー weight は Bake 済みのレイヤー値に再生時に掛かる）。Clip の範囲外と、トラックの無いレイヤーは宣言値 1。
    /// </remarks>
    [TrackClipType(typeof(FacialLayerWeightClip))]
    [TrackBindingType(typeof(FacialTimelineReceiver))]
    [TrackColor(0.85f, 0.55f, 0.2f)]
    public sealed class FacialLayerWeightTrack : TrackAsset, IPropertyPreview
    {
        [SerializeField] private string layerName = string.Empty;

        /// <summary>対象レイヤー名（Profile の layers の name と一致）。</summary>
        public string LayerName
        {
            get => layerName;
            set => layerName = value ?? string.Empty;
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<FacialLayerWeightMixerBehaviour> playable =
                ScriptPlayable<FacialLayerWeightMixerBehaviour>.Create(graph, inputCount);
            playable.GetBehaviour().Configure(layerName, CollectClipSamples(this));
            return playable;
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            FacialTimelineEditorPreviewBridge.GatherProperties?.Invoke(director, this, driver);
        }

        /// <summary>Clip を開始時刻・終了時刻・カーブの組に写す（Mixer と Edit プレビューが同じ規則で評価するため）。</summary>
        public static FacialLayerWeightMixerBehaviour.ClipSample[] CollectClipSamples(FacialLayerWeightTrack track)
        {
            if (track == null)
            {
                return Array.Empty<FacialLayerWeightMixerBehaviour.ClipSample>();
            }

            var clips = new List<FacialLayerWeightMixerBehaviour.ClipSample>();
            foreach (TimelineClip clip in track.GetClips())
            {
                if (clip.asset is FacialLayerWeightClip weightClip)
                {
                    clips.Add(new FacialLayerWeightMixerBehaviour.ClipSample(clip.start, clip.end, weightClip.Weight));
                }
            }

            return clips.Count == 0
                ? Array.Empty<FacialLayerWeightMixerBehaviour.ClipSample>()
                : clips.ToArray();
        }
    }
}
