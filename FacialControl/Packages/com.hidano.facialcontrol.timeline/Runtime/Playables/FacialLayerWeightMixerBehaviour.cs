using System;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.EditorPreview;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Playables
{
    /// <summary>
    /// レイヤー weight トラックの Mixer。Play では Receiver のセッションを通じて毎フレームレイヤー weight を注入する。
    /// Edit の描画は <see cref="FacialTimelineEditorPreviewBridge.ApplyPreview"/>（オフライン合成）が行う。
    /// </summary>
    public sealed class FacialLayerWeightMixerBehaviour : PlayableBehaviour
    {
        private ClipSample[] _clips = Array.Empty<ClipSample>();
        private string _layerName = string.Empty;
        private FacialTimelineReceiver _receiver;
        private bool _isPlaying;

        public override void OnPlayableCreate(Playable playable)
        {
            _isPlaying = FacialTimelinePlayMode.IsPlaying;
        }

        public void Configure(string layerName, ClipSample[] clips)
        {
            _layerName = layerName ?? string.Empty;
            _clips = clips ?? Array.Empty<ClipSample>();
            _receiver = null;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            FacialTimelineReceiver receiver = ResolveReceiver(playerData);
            if (receiver == null)
            {
                _receiver = null;
                return;
            }

            PlayableDirector director = playable.GetGraph().GetResolver() as PlayableDirector;
            TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (!_isPlaying)
            {
                FacialTimelineEditorPreviewBridge.ApplyPreview?.Invoke(receiver, timeline, playable.GetTime());
                return;
            }

            receiver.BeginPlaybackSession(timeline, director);
            if (!receiver.IsSessionOwnedBy(director))
            {
                return;
            }

            _receiver = receiver;
            double evaluatedTrackTime = playable.GetTime();
            if (info.deltaTime > 0d)
            {
                evaluatedTrackTime += info.deltaTime;
            }

            receiver.ApplyLayerWeight(_layerName, Evaluate(_clips, evaluatedTrackTime));
        }

        public override void OnGraphStop(Playable playable)
        {
            ReleaseReceiver();
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            ReleaseReceiver();
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            ReleaseReceiver();
        }

        /// <summary>
        /// 指定時刻のレイヤー weight。時刻を含む Clip（重なれば後ろの Clip）のカーブ値、どの Clip にも入らなければ宣言値 1。
        /// </summary>
        public static float Evaluate(ClipSample[] clips, double trackTime)
        {
            if (clips != null)
            {
                for (int i = clips.Length - 1; i >= 0; i--)
                {
                    ClipSample clip = clips[i];
                    if (trackTime < clip.StartTime || trackTime >= clip.EndTime)
                    {
                        continue;
                    }

                    return clip.Curve != null && clip.Curve.length > 0
                        ? clip.Curve.Evaluate((float)(trackTime - clip.StartTime))
                        : TimelineLayerWeightOverride.DeclaredLayerWeight;
                }
            }

            return TimelineLayerWeightOverride.DeclaredLayerWeight;
        }

        private void ReleaseReceiver()
        {
            if (_receiver != null)
            {
                _receiver.ReleaseAll();
            }

            _receiver = null;
        }

        private static FacialTimelineReceiver ResolveReceiver(object playerData)
        {
            switch (playerData)
            {
                case FacialTimelineReceiver receiver:
                    return receiver;
                case GameObject gameObject:
                    return gameObject.GetComponent<FacialTimelineReceiver>();
                case Component component:
                    return component.GetComponent<FacialTimelineReceiver>();
                default:
                    return null;
            }
        }

        public readonly struct ClipSample
        {
            public ClipSample(double startTime, double endTime, AnimationCurve curve)
            {
                StartTime = startTime;
                EndTime = endTime;
                Curve = curve;
            }

            public double StartTime { get; }

            public double EndTime { get; }

            public AnimationCurve Curve { get; }
        }
    }
}
