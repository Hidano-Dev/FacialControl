using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Domain.Models;

namespace Hidano.FacialControl.Timeline.Domain.Services
{
    /// <summary>
    /// Play 中の Expression トラック 1 本ぶんの表情の値を、ランタイムの trigger 入力源と同じ遷移計算で作る（HID-318）。
    /// </summary>
    /// <remarks>
    /// <para>REC 再生（<c>RecPlaybackScheduler</c> → <c>ExpressionTriggerInputSourceBase.Tick</c>）と同じく、フレームごとに
    /// 「前回の評価時刻より後で今回の評価時刻以下のイベントを発火してから、そのフレームの dt で進める」。遷移はイベントを拾った
    /// フレームの dt をまるごと進めるので、実際の開始はイベント直前のフレームの時刻になる。この位相は再生側のフレームの刻みで
    /// 決まるため、事前に Bake したカーブでは再現できない。</para>
    /// <para>シーク（<see cref="JumpTo"/>）は、先頭から記録時刻ちょうどの位相で計算し直す（前のフレームが無いので、Bake と同じ位相）。</para>
    /// <para>毎フレームの <see cref="Advance"/> / <see cref="TryWriteValues"/> はヒープ確保しない。メインスレッド専用。</para>
    /// </remarks>
    public sealed class TimelineExpressionTransitionPlayer
    {
        private readonly TimelineStateEvent[] _events;
        private readonly TransitionSource _source;
        private readonly List<string> _startsActiveStack;
        private readonly string[] _emptyStack = Array.Empty<string>();

        /// <param name="events">Expression トラックの状態イベント（<see cref="TimelineStateEventCollector"/> の結果。時刻順）。</param>
        /// <param name="blendShapeNames">値を出す BlendShape 名の並び（<see cref="TryWriteValues"/> の出力順）。</param>
        /// <param name="profile">Expression の値と遷移を引く Profile。</param>
        /// <param name="maxStackDepth">Expression スタックの最大深度（ランタイムの trigger 入力源と揃える）。</param>
        /// <param name="exclusionMode">レイヤーの排他モード。</param>
        public TimelineExpressionTransitionPlayer(
            TimelineStateEvent[] events,
            IReadOnlyList<string> blendShapeNames,
            FacialProfile profile,
            int maxStackDepth,
            ExclusionMode exclusionMode)
        {
            if (blendShapeNames == null)
            {
                throw new ArgumentNullException(nameof(blendShapeNames));
            }

            _events = events ?? Array.Empty<TimelineStateEvent>();
            _source = new TransitionSource(blendShapeNames, profile, maxStackDepth, exclusionMode);
            _startsActiveStack = new List<string>();
            for (int i = 0; i < _events.Length; i++)
            {
                // StartsActive は TimelineStateEventCollector が 0 秒から始まる Clip にだけ立てる。
                if (_events[i].IsOn && _events[i].StartsActive && !string.IsNullOrEmpty(_events[i].ExpressionId))
                {
                    _startsActiveStack.Add(_events[i].ExpressionId);
                }
            }
        }

        /// <summary><see cref="TryWriteValues"/> が書く値の数（コンストラクタの BlendShape 名の数）。</summary>
        public int BlendShapeCount => _source.BlendShapeCount;

        /// <summary>
        /// 指定時刻の状態を先頭から計算し直す。StartsActive の表情は遷移なしで反映し（REC 再生の開始と同じ）、
        /// 残りのイベントは記録時刻ちょうどで発火して、イベントの間の時間だけ進める。
        /// </summary>
        public void JumpTo(double time)
        {
            _source.ResetToExpressionStack(_startsActiveStack.Count > 0 ? (IReadOnlyList<string>)_startsActiveStack : _emptyStack);
            double current = 0d;
            for (int i = 0; i < _events.Length; i++)
            {
                TimelineStateEvent stateEvent = _events[i];
                if (stateEvent.TimeSeconds > time)
                {
                    break;
                }

                if (IsSkipped(stateEvent))
                {
                    continue;
                }

                if (stateEvent.TimeSeconds > current)
                {
                    _source.Tick((float)(stateEvent.TimeSeconds - current));
                    current = stateEvent.TimeSeconds;
                }

                Dispatch(stateEvent);
            }

            // シーク先がイベントちょうどでも Tick する（遷移時間 0 の表情は Tick(0) で遷移を終える。ランタイムと同じ）。
            _source.Tick((float)Math.Max(0d, time - current));
        }

        /// <summary>
        /// 1 フレームぶん進める。<paramref name="fromExclusive"/> より後で <paramref name="toInclusive"/> 以下のイベントを
        /// 時刻順に発火してから、<paramref name="deltaTime"/> だけ遷移を進める（REC 再生と同じ順序）。
        /// </summary>
        /// <param name="fromExclusive">前回の評価時刻（Timeline 時刻）。</param>
        /// <param name="toInclusive">今回の評価時刻（Timeline 時刻）。</param>
        /// <param name="deltaTime">このフレームで進める時間（秒）。</param>
        public void Advance(double fromExclusive, double toInclusive, float deltaTime)
        {
            if (toInclusive > fromExclusive)
            {
                int start = UpperBound(fromExclusive);
                for (int i = start; i < _events.Length; i++)
                {
                    TimelineStateEvent stateEvent = _events[i];
                    if (stateEvent.TimeSeconds > toInclusive)
                    {
                        break;
                    }

                    if (!IsSkipped(stateEvent))
                    {
                        Dispatch(stateEvent);
                    }
                }
            }

            _source.Tick(deltaTime);
        }

        /// <summary>現在の値を書く。表情が無く遷移も終わっていれば false（レイヤーに寄与しない）。</summary>
        public bool TryWriteValues(Span<float> output)
        {
            return _source.TryWriteValues(output);
        }

        /// <summary>
        /// 時刻順の発火から外すイベント。StartsActive の On はスタックとして先に反映済み。録画の終わりまで押していた表情の Off は、
        /// 離した操作ではないので発火しない（REC 再生は記録の終わりでも表情を保持する）。
        /// </summary>
        private static bool IsSkipped(in TimelineStateEvent stateEvent)
        {
            return (stateEvent.IsOn && stateEvent.StartsActive) || stateEvent.IsHeldToEnd;
        }

        private void Dispatch(in TimelineStateEvent stateEvent)
        {
            string expressionId = stateEvent.ExpressionId;
            if (string.IsNullOrEmpty(expressionId))
            {
                return;
            }

            if (stateEvent.IsOn)
            {
                _source.TriggerOn(expressionId);
            }
            else if (stateEvent.IsOff)
            {
                _source.TriggerOff(expressionId);
            }
        }

        private int UpperBound(double time)
        {
            int low = 0;
            int high = _events.Length;
            while (low < high)
            {
                int mid = low + ((high - low) / 2);
                if (_events[mid].TimeSeconds <= time)
                {
                    low = mid + 1;
                }
                else
                {
                    high = mid;
                }
            }

            return low;
        }

        private sealed class TransitionSource : ExpressionTriggerInputSourceBase
        {
            private static readonly InputSourceId SourceId = InputSourceId.Parse("timeline:transition");

            private readonly BitArray _contributeMask;

            public TransitionSource(IReadOnlyList<string> blendShapeNames, FacialProfile profile, int maxStackDepth, ExclusionMode exclusionMode)
                : base(
                    SourceId,
                    blendShapeCount: blendShapeNames.Count,
                    maxStackDepth: Math.Max(1, maxStackDepth),
                    exclusionMode: exclusionMode,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
                _contributeMask = new BitArray(blendShapeNames.Count, true);
            }

            public override BitArray ContributeMask => _contributeMask;
        }
    }
}
