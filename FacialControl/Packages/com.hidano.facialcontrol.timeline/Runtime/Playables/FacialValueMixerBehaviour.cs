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
        private TimelineValueProviderInputSource _valueProviderSink;

        // 値提供型: Clip ごとの「軸 → ホスト BlendShape index」の対応表（Configure で確保し、sink が変わったときだけ埋め直す）。
        private int[][] _hostIndexMaps = Array.Empty<int[]>();
        private TimelineValueProviderInputSource _mappedSink;
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
            _hostIndexMaps = Array.Empty<int[]>();
            if (_channelKind == FacialValueChannelKind.ValueProvider && _clips.Length > 0)
            {
                _hostIndexMaps = new int[_clips.Length][];
                for (int i = 0; i < _clips.Length; i++)
                {
                    _hostIndexMaps[i] = new int[_clips[i].Axes.Length];
                }
            }

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

            if (!TryGetActiveClip(evaluatedTrackTime, out int clipIndex, out float clipTime))
            {
                InvalidateResolvedSink();
                return;
            }

            ClipSample clip = _clips[clipIndex];
            switch (_channelKind)
            {
                case FacialValueChannelKind.Gaze:
                    PublishGaze(clip.Axes, clipTime);
                    break;

                case FacialValueChannelKind.ValueProvider:
                    PublishValueProvider(clip, clipIndex, clipTime);
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
                _valueProviderSink = null;
            }

            // 乗っ取り sink はセッションごとに作り直されるため、毎回 Receiver から引く（線形探索のみで確保なし）。
            switch (_channelKind)
            {
                case FacialValueChannelKind.Gaze:
                    return receiver.TryGetGazeSink(_channelSubId, out _gazeSink);

                case FacialValueChannelKind.ValueProvider:
                    return receiver.TryGetValueProviderSink(_channelSubId, out _valueProviderSink);

                default:
                    return receiver.TryGetAnalogSink(_channelSubId, out _analogSink);
            }
        }

        private bool TryGetActiveClip(double evaluatedTrackTime, out int clipIndex, out float clipTime)
        {
            for (int i = _clips.Length - 1; i >= 0; i--)
            {
                ClipSample candidate = _clips[i];
                if (evaluatedTrackTime < candidate.StartTime || evaluatedTrackTime >= candidate.EndTime)
                {
                    continue;
                }

                clipIndex = i;
                clipTime = (float)(evaluatedTrackTime - candidate.StartTime);
                return true;
            }

            clipIndex = -1;
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

        private void PublishValueProvider(in ClipSample clip, int clipIndex, float clipTime)
        {
            if (_valueProviderSink == null || (uint)clipIndex >= (uint)_hostIndexMaps.Length)
            {
                return;
            }

            if (!ReferenceEquals(_mappedSink, _valueProviderSink))
            {
                // sink（= ホストの BlendShape 並び）が変わったときだけ対応表を埋め直す。確保は Configure 済み。
                for (int i = 0; i < _clips.Length; i++)
                {
                    _valueProviderSink.FillHostIndexMap(_clips[i].BlendShapeNames, _clips[i].BlendShapeIndices, _hostIndexMaps[i]);
                }

                _mappedSink = _valueProviderSink;
            }

            _valueProviderSink.PublishClip(clip.Axes, clip.Contributes, clip.Validity, _hostIndexMaps[clipIndex], clipTime);
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
            switch (_channelKind)
            {
                case FacialValueChannelKind.Gaze:
                    _gazeSink?.Invalidate();
                    return;

                case FacialValueChannelKind.ValueProvider:
                    _valueProviderSink?.Invalidate();
                    return;

                default:
                    _analogSink?.Invalidate();
                    return;
            }
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
            _valueProviderSink = null;
            _mappedSink = null;
        }

        public readonly struct ClipSample
        {
            public ClipSample(double startTime, double endTime, AnimationCurve[] axes)
                : this(startTime, endTime, axes, null, null, null, null)
            {
            }

            public ClipSample(
                double startTime,
                double endTime,
                AnimationCurve[] axes,
                string[] blendShapeNames,
                int[] blendShapeIndices,
                AnimationCurve[] contributes,
                AnimationCurve validity)
            {
                StartTime = startTime;
                EndTime = endTime;
                Axes = axes ?? Array.Empty<AnimationCurve>();
                BlendShapeNames = blendShapeNames ?? Array.Empty<string>();
                BlendShapeIndices = blendShapeIndices ?? Array.Empty<int>();
                Contributes = contributes ?? Array.Empty<AnimationCurve>();
                Validity = validity;
            }

            public double StartTime { get; }

            public double EndTime { get; }

            public AnimationCurve[] Axes { get; }

            /// <summary>値提供型のみ。軸ごとの BlendShape 名。</summary>
            public string[] BlendShapeNames { get; }

            /// <summary>値提供型のみ。軸ごとの記録時の BlendShape index。</summary>
            public int[] BlendShapeIndices { get; }

            /// <summary>値提供型のみ。軸ごとの寄与 mask カーブ。</summary>
            public AnimationCurve[] Contributes { get; }

            /// <summary>値提供型のみ。有効状態カーブ（null / キー無しは常に有効）。</summary>
            public AnimationCurve Validity { get; }
        }
    }
}
