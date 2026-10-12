using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.EditorPreview;
using Hidano.FacialControl.Timeline.Playables;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tracks
{
    [TrackClipType(typeof(FacialValueClip))]
    [TrackBindingType(typeof(FacialTimelineReceiver))]
    [TrackColor(0.23f, 0.56f, 0.78f)]
    public sealed class FacialValueTrack : TrackAsset, IPropertyPreview, IFacialTimelineBakeHolder
    {
        [SerializeField] private string channelSubId = string.Empty;
        [SerializeField] private FacialValueChannelKind channelKind = FacialValueChannelKind.Analog;

        // REC に記録された目線ボーンのパス（Animator のルート相対、HID-192）。Gaze トラックのみ。空なら目線メニューの
        // ジョイント名 → Avatar の目線ボーンで解決する。
        [SerializeField] private string leftEyeBonePath = string.Empty;
        [SerializeField] private string rightEyeBonePath = string.Empty;

        // 内部キャッシュ（Bake サブアセット）への参照。Exporter / 再ベイクが全 Facial トラックへ同じ参照を書く（D5）。
        [SerializeField, HideInInspector] private FacialTimelineBakeAsset bake;

        public FacialTimelineBakeAsset Bake
        {
            get => bake;
            set => bake = value;
        }

        public string ChannelSubId
        {
            get => channelSubId;
            set => channelSubId = value ?? string.Empty;
        }

        public FacialValueChannelKind ChannelKind
        {
            get => channelKind;
            set => channelKind = value;
        }

        /// <summary>
        /// REC に記録された左目の目線ボーンのパス（Animator のルート相対）。Gaze トラックで REC から書き出したときだけ入る。
        /// Edit プレビューと AnimationTrack 変換はこのパスで目線ボーンを解決する。空なら目線メニューの規則で解決する。
        /// </summary>
        public string LeftEyeBonePath
        {
            get => leftEyeBonePath ?? string.Empty;
            set => leftEyeBonePath = value ?? string.Empty;
        }

        /// <summary>REC に記録された右目の目線ボーンのパス（Animator のルート相対）。<see cref="LeftEyeBonePath"/> を参照。</summary>
        public string RightEyeBonePath
        {
            get => rightEyeBonePath ?? string.Empty;
            set => rightEyeBonePath = value ?? string.Empty;
        }

        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
            ScriptPlayable<FacialValueMixerBehaviour> playable =
                ScriptPlayable<FacialValueMixerBehaviour>.Create(graph, inputCount);
            playable.GetBehaviour().Configure(channelSubId, channelKind, CollectClipSamples(this));
            return playable;
        }

        public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
        {
            FacialTimelineEditorPreviewBridge.GatherProperties?.Invoke(director, this, driver);
        }

        private static FacialValueMixerBehaviour.ClipSample[] CollectClipSamples(FacialValueTrack track)
        {
            if (track == null)
            {
                return Array.Empty<FacialValueMixerBehaviour.ClipSample>();
            }

            var clips = new List<FacialValueMixerBehaviour.ClipSample>();
            foreach (TimelineClip clip in track.GetClips())
            {
                if (!(clip.asset is FacialValueClip valueClip))
                {
                    continue;
                }

                clips.Add(new FacialValueMixerBehaviour.ClipSample(
                    clip.start,
                    clip.end,
                    valueClip.Axes,
                    valueClip.BlendShapeNames,
                    valueClip.BlendShapeIndices,
                    valueClip.Contributes,
                    valueClip.Validity));
            }

            return clips.Count == 0
                ? Array.Empty<FacialValueMixerBehaviour.ClipSample>()
                : clips.ToArray();
        }
    }
}
