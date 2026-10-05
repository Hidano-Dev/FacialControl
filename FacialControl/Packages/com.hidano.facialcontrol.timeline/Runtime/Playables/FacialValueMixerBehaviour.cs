using System;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.EditorPreview;
using Hidano.FacialControl.Timeline.Tracks;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Playables
{
    public sealed class FacialValueMixerBehaviour : PlayableBehaviour
    {
        private ClipSample[] _clips = Array.Empty<ClipSample>();
        private string _channelSubId = string.Empty;
        private FacialValueChannelKind _channelKind;
        private FacialTimelineReceiver _receiver;
        private TimelineAnalogInputSource _analogSink;
        private TimelineGazeInputSource _gazeSink;
        private float[] _axisBuffer = Array.Empty<float>();
        private bool _isPlaying;

        public override void OnPlayableCreate(Playable playable)
        {
            // Edit / Play の判定は playable 生成時に 1 回だけ読む（Play 遷移時は graph が作り直される）。
            _isPlaying = FacialTimelinePlayMode.IsPlaying;
        }

        public void Configure(string channelSubId, FacialValueChannelKind channelKind, ClipSample[] clips)
        {
            _clips = clips ?? Array.Empty<ClipSample>();
            _channelSubId = channelSubId ?? string.Empty;
            _channelKind = channelKind;
            ReleaseCachedSinks();
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            FacialTimelineReceiver receiver = ResolveReceiver(playerData);
            if (receiver == null)
            {
                ReleaseCachedSinks();
                return;
            }

            PlayableDirector director = ResolveDirector(playable);
            TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (!_isPlaying)
            {
                // Edit はプレビューのみ。セッションは開始しない（乗っ取りも行わない）。
                FacialTimelineEditorPreviewBridge.ApplyPreview?.Invoke(receiver, timeline, playable.GetTime());
                return;
            }

            // Play: 冪等なセッション開始。Active 以外（Pending / Failed / 他 Director の所有）は戻る。Mixer はログを出さない。
            receiver.BeginPlaybackSession(timeline, director);
            if (!receiver.IsSessionOwnedBy(director))
            {
                return;
            }

            if (!TryResolveSink(receiver))
            {
                return;
            }

            double evaluatedTrackTime = playable.GetTime();
            if (info.deltaTime > 0d)
            {
                evaluatedTrackTime += info.deltaTime;
            }

            if (!TryGetActiveClip(evaluatedTrackTime, out ClipSample clip, out float clipTime))
            {
                InvalidateResolvedSink();
                return;
            }

            switch (_channelKind)
            {
                case FacialValueChannelKind.Gaze:
                    PublishGaze(clip.Axes, clipTime);
                    break;

                default:
                    PublishAnalog(clip.Axes, clipTime);
                    break;
            }
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

        private bool TryResolveSink(FacialTimelineReceiver receiver)
        {
            if (!ReferenceEquals(_receiver, receiver))
            {
                _receiver = receiver;
                _analogSink = null;
                _gazeSink = null;
            }

            // 乗っ取り sink はセッションごとに作り直されるため、毎回 Receiver から引く（線形探索のみで確保なし）。
            if (_channelKind == FacialValueChannelKind.Gaze)
            {
                return receiver.TryGetGazeSink(_channelSubId, out _gazeSink);
            }

            return receiver.TryGetAnalogSink(_channelSubId, out _analogSink);
        }

        private bool TryGetActiveClip(double evaluatedTrackTime, out ClipSample clip, out float clipTime)
        {
            for (int i = _clips.Length - 1; i >= 0; i--)
            {
                ClipSample candidate = _clips[i];
                if (evaluatedTrackTime < candidate.StartTime || evaluatedTrackTime >= candidate.EndTime)
                {
                    continue;
                }

                clip = candidate;
                clipTime = (float)(evaluatedTrackTime - candidate.StartTime);
                return true;
            }

            clip = default;
            clipTime = 0f;
            return false;
        }

        private void PublishAnalog(AnimationCurve[] axes, float clipTime)
        {
            if (_analogSink == null)
            {
                return;
            }

            int axisCount = _analogSink.AxisCount;
            EnsureAxisBuffer(axisCount);

            axes ??= Array.Empty<AnimationCurve>();
            for (int i = 0; i < axisCount; i++)
            {
                AnimationCurve curve = i < axes.Length ? axes[i] : null;
                _axisBuffer[i] = curve != null ? curve.Evaluate(clipTime) : 0f;
            }

            if (!_analogSink.SetAxes(_axisBuffer.AsSpan(0, axisCount)))
            {
                _analogSink.Invalidate();
            }
        }

        private void PublishGaze(AnimationCurve[] axes, float clipTime)
        {
            if (_gazeSink == null)
            {
                return;
            }

            axes ??= Array.Empty<AnimationCurve>();
            float x = axes.Length > 0 && axes[0] != null ? axes[0].Evaluate(clipTime) : 0f;
            float y = axes.Length > 1 && axes[1] != null ? axes[1].Evaluate(clipTime) : 0f;
            _gazeSink.Publish(x, y);
        }

        private void EnsureAxisBuffer(int axisCount)
        {
            if (_axisBuffer.Length >= axisCount)
            {
                return;
            }

            _axisBuffer = new float[axisCount];
        }

        private void InvalidateResolvedSink()
        {
            if (_channelKind == FacialValueChannelKind.Gaze)
            {
                _gazeSink?.Invalidate();
                return;
            }

            _analogSink?.Invalidate();
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

        private static PlayableDirector ResolveDirector(Playable playable)
        {
            return playable.GetGraph().GetResolver() as PlayableDirector;
        }

        private void ReleaseReceiver()
        {
            if (_receiver != null)
            {
                _receiver.ReleaseAll();
            }

            ReleaseCachedSinks();
        }

        private void ReleaseCachedSinks()
        {
            _receiver = null;
            _analogSink = null;
            _gazeSink = null;
        }

        public readonly struct ClipSample
        {
            public ClipSample(double startTime, double endTime, AnimationCurve[] axes)
            {
                StartTime = startTime;
                EndTime = endTime;
                Axes = axes ?? Array.Empty<AnimationCurve>();
            }

            public double StartTime { get; }

            public double EndTime { get; }

            public AnimationCurve[] Axes { get; }
        }
    }
}
