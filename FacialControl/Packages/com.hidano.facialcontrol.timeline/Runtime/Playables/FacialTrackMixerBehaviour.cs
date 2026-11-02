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

    /// <remarks>
    /// <para>Play 中は、表情の状態（trigger sink）と値（<see cref="TimelineExpressionTransitionPlayer"/>）を同じ評価時刻で進める。
    /// 値はフレームごとに「通過したイベントを発火してからそのフレームの dt で進める」ため、REC 再生と同じ位相になる（HID-318）。</para>
    /// </remarks>
    public sealed class FacialTrackMixerBehaviour : PlayableBehaviour
    {
        /// <summary>終端に留めたとき、終端ちょうどのイベントを拾うための余裕（秒）。</summary>
        internal const double HoldEndEventMargin = 1e-6d;

        private readonly TimelineEventStateReconstructor _stateReconstructor = new TimelineEventStateReconstructor();
        private TimelineExpressionTransitionPlayer _player;
        private double _lastRawTime;

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
            _player = null;
            _lastEvaluatedTime = 0d;
            _lastRawTime = 0d;
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
                receiver.TryCreateExpressionTransitionPlayer(_layerName, _stateEvents, out _player);
            }

            double rawTime = playable.GetTime();
            double holdEndTime = FacialValueMixerBehaviour.ResolveHoldEndTime(director);
            double evaluatedTime = ResolveEvaluationTime(rawTime, holdEndTime);

            if (!_hasEvaluationTime)
            {
                _stateReconstructor.JumpTo(evaluatedTime, _sink);
                _player?.JumpTo(evaluatedTime);
                receiver.ApplyExpressionValues(_layerName, _player);
                _lastEvaluatedTime = evaluatedTime;
                _lastRawTime = rawTime;
                _hasEvaluationTime = true;
                return;
            }

            // 終端（Hold）では Director の時刻が止まる。途中の遷移は REC 再生と同じく、フレームの dt で実時間どおり進め切る。
            double frameDelta = info.deltaTime > 0d ? info.deltaTime : Time.deltaTime;
            bool atHoldEnd = rawTime >= holdEndTime;
            double rawDelta = rawTime - _lastRawTime;
            if (Math.Abs(rawDelta) <= 1e-9d)
            {
                if (atHoldEnd && frameDelta > 0d)
                {
                    _player?.Advance(_lastEvaluatedTime, _lastEvaluatedTime, (float)frameDelta);
                }

                receiver.ApplyExpressionValues(_layerName, _player);
                return;
            }

            // 終端に届いたフレームは、Director の時刻の進みが終端で切られて dt より短い。シークではないので 1 フレームの前進として扱う。
            bool reachesHoldEnd = atHoldEnd && rawDelta > 0d && frameDelta > 0d && rawDelta <= frameDelta * 1.1d;
            if (reachesHoldEnd || IsLinearAdvance(rawDelta, info.deltaTime, Time.deltaTime))
            {
                _stateReconstructor.AdvanceLinear(_lastEvaluatedTime, evaluatedTime, _sink);
                _player?.Advance(_lastEvaluatedTime, evaluatedTime, (float)(reachesHoldEnd ? frameDelta : rawDelta));
            }
            else
            {
                _stateReconstructor.JumpTo(evaluatedTime, _sink);
                _player?.JumpTo(evaluatedTime);
            }

            receiver.ApplyExpressionValues(_layerName, _player);
            _lastEvaluatedTime = evaluatedTime;
            _lastRawTime = rawTime;
        }

        /// <summary>
        /// 状態イベントを拾う評価時刻。Hold の Director は終端に留まる（Value / レイヤー weight の Mixer と同じ）ので、終端以降は
        /// 終端に留める。終端ちょうどのイベント（記録の最後に離した表情の Off）を取りこぼさないよう、<see cref="HoldEndEventMargin"/>
        /// だけ先まで拾う。
        /// </summary>
        /// <remarks>
        /// 記録の終わりまで押していた表情の Clip の終わりの Off は、<see cref="TimelineStateEvent.IsHeldToEnd"/> として発火しない
        /// （REC 再生は記録の終わりでも表情を保持する。HID-298 / HID-318）。
        /// </remarks>
        internal static double ResolveEvaluationTime(double rawTime, double holdEndTime)
        {
            return rawTime >= holdEndTime ? holdEndTime + HoldEndEventMargin : rawTime;
        }

        /// <summary>
        /// 前回の評価から順方向に 1 フレームぶん進んだか（シークでないか）。Director の時刻の進みが、そのフレームの dt と合うときだけ
        /// 1 フレームの前進とみなす。dt は GameTime などの再生なら Director の dt（<see cref="FrameData.deltaTime"/>）、Manual の
        /// <c>PlayableDirector.Evaluate</c>（Director の dt が 0）ならゲームのフレームの dt（<see cref="Time.deltaTime"/>）。
        /// </summary>
        /// <remarks>時刻を飛ばした評価（スクラブ・シーク）はシークとして扱い、遷移を記録時刻ちょうどの位相で計算し直す。</remarks>
        internal static bool IsLinearAdvance(double delta, double frameDeltaTime, double gameDeltaTime)
        {
            if (delta <= 0d)
            {
                return false;
            }

            double reference = frameDeltaTime > 0d ? frameDeltaTime : gameDeltaTime;
            if (reference <= 0d)
            {
                return false;
            }

            return Math.Abs(delta - reference) <= Math.Max(1e-6d, reference * 0.1d);
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
            _player = null;
            _lastEvaluatedTime = 0d;
            _lastRawTime = 0d;
            _hasEvaluationTime = false;
        }
    }
}
