using System;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.EditorPreview;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Playables
{
    /// <summary>
    /// Mixer が Edit プレビューと Play を分岐する判定。Mixer は playable 生成時に 1 回だけ読み、bool でキャッシュする。
    /// </summary>
    internal static class FacialTimelinePlayMode
    {
        /// <summary>テスト専用の上書き（null なら <see cref="UnityEngine.Application.isPlaying"/>）。EditMode テストで Play 相当を再現する。</summary>
        internal static bool? OverrideIsPlaying { get; set; }

        internal static bool IsPlaying => OverrideIsPlaying ?? UnityEngine.Application.isPlaying;
    }

    public sealed class FacialTrackMixerBehaviour : PlayableBehaviour
    {
        private readonly TimelineEventStateReconstructor _stateReconstructor = new TimelineEventStateReconstructor();

        private string _layerName = string.Empty;
        private TimelineStateEvent[] _stateEvents = Array.Empty<TimelineStateEvent>();
        private FacialTimelineReceiver _receiver;
        private ITimelineTriggerSink _sink;
        private double _lastEvaluatedTime;
        private bool _hasEvaluationTime;
        private bool _isPlaying;

        public override void OnPlayableCreate(Playable playable)
        {
            // Edit / Play の判定は playable 生成時に 1 回だけ読む（Play 遷移時は graph が作り直される）。
            _isPlaying = FacialTimelinePlayMode.IsPlaying;
        }

        public void ConfigureExpression(string layerName, TimelineStateEvent[] stateEvents)
        {
            _layerName = layerName ?? string.Empty;
            _stateEvents = stateEvents ?? Array.Empty<TimelineStateEvent>();
            _stateReconstructor.SetEvents(_stateEvents);
            _receiver = null;
            _sink = null;
            _lastEvaluatedTime = 0d;
            _hasEvaluationTime = false;
        }

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            if (_stateEvents.Length == 0)
            {
                return;
            }

            FacialTimelineReceiver receiver = ResolveReceiver(playerData);
            if (receiver == null)
            {
                return;
            }

            PlayableDirector director = ResolveDirector(playable);
            TimelineAsset timeline = director != null ? director.playableAsset as TimelineAsset : null;
            if (!_isPlaying)
            {
                // Edit はプレビューのみ。セッションは開始しない（Receiver の状態・診断・registry に触れない）。
                FacialTimelineEditorPreviewBridge.ApplyPreview?.Invoke(receiver, timeline, playable.GetTime());
                return;
            }

            // Play: 冪等なセッション開始。Active 以外（Pending / Failed / 他 Director の所有）は戻る。
            // 失敗理由は Receiver の診断に記録済みのため Mixer はログを出さない。
            receiver.BeginPlaybackSession(timeline, director);
            if (!receiver.IsSessionOwnedBy(director))
            {
                return;
            }

            if (!receiver.TryGetExpressionSink(_layerName, out Timeline.Adapters.InputSources.TimelineExpressionStateSink expressionSink))
            {
                return;
            }

            if (!ReferenceEquals(_receiver, receiver) || !ReferenceEquals(_sink, expressionSink))
            {
                _receiver = receiver;
                _sink = expressionSink;
                _hasEvaluationTime = false;
            }

            double evaluatedTime = playable.GetTime();

            receiver.SampleExpressionValues(_layerName, evaluatedTime);

            if (!_hasEvaluationTime)
            {
                _stateReconstructor.JumpTo(evaluatedTime, _sink);
                _lastEvaluatedTime = evaluatedTime;
                _hasEvaluationTime = true;
                return;
            }

            double deltaTime = evaluatedTime - _lastEvaluatedTime;
            if (Math.Abs(deltaTime) <= 1e-9d)
            {
                return;
            }

            double expectedLinearDelta = info.deltaTime;
            bool isLinearAdvance =
                deltaTime > 0d &&
                expectedLinearDelta > 0d &&
                Math.Abs(deltaTime - expectedLinearDelta) <= Math.Max(1e-6d, Math.Abs(expectedLinearDelta) * 0.1d);

            if (isLinearAdvance)
            {
                _stateReconstructor.AdvanceLinear(_lastEvaluatedTime, evaluatedTime, _sink);
            }
            else
            {
                _stateReconstructor.JumpTo(evaluatedTime, _sink);
            }

            _lastEvaluatedTime = evaluatedTime;
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

            _receiver = null;
            _sink = null;
            _lastEvaluatedTime = 0d;
            _hasEvaluationTime = false;
        }
    }
}
