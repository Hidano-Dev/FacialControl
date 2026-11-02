using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Playables;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineExpressionTransitionPlayer"/> が REC 再生と同じ位相（イベントを拾ったフレームの dt をまるごと進める）で
    /// 表情の値を作ることを検証する（HID-318）。
    /// </summary>
    [SmallTest]
    public sealed class TimelineExpressionTransitionPlayerTests : SizedTestFixture
    {
        private const string Layer = "emotion";
        private const float Duration = 0.1f;
        private static readonly string[] BlendShapes = { "smile", "angry", "instant" };

        [Test]
        public void Advance_EventInsideFrame_AdvancesWholeFrameDelta()
        {
            var player = CreatePlayer(On(0.105d, "smile"), Off(1d, "smile"));
            player.JumpTo(0.1d);

            // イベント（0.105）は前回（0.1）と今回（0.12）の間。REC 再生と同じく dt（0.02）をまるごと進める。
            player.Advance(0.1d, 0.12d, 0.02f);

            Assert.That(Value(player, 0), Is.EqualTo(0.02f / Duration).Within(1e-5f));
        }

        [Test]
        public void Advance_EventAtCurrentTime_FiresInThatFrame()
        {
            var player = CreatePlayer(On(0.12d, "smile"), Off(1d, "smile"));
            player.JumpTo(0.1d);

            player.Advance(0.1d, 0.12d, 0.02f);

            Assert.That(Value(player, 0), Is.EqualTo(0.02f / Duration).Within(1e-5f), "今回の評価時刻ちょうどのイベントは今回拾う");
        }

        [Test]
        public void Advance_EventAtPreviousTime_IsNotFiredAgain()
        {
            var player = CreatePlayer(On(0.1d, "smile"), Off(1d, "smile"));
            player.JumpTo(0.1d);

            player.Advance(0.1d, 0.12d, 0.02f);
            player.Advance(0.12d, 0.14d, 0.02f);

            Assert.That(Value(player, 0), Is.EqualTo(0.04f / Duration).Within(1e-5f), "前回拾ったイベントで遷移をやり直さない");
        }

        [Test]
        public void JumpTo_MidTransition_UsesExactEventTime()
        {
            var player = CreatePlayer(On(0.2d, "smile"), Off(1d, "smile"));

            player.JumpTo(0.25d);

            Assert.That(Value(player, 0), Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void JumpTo_StartsActiveExpression_AppliesWithoutTransition()
        {
            var player = CreatePlayer(new TimelineStateEvent(0d, TimelineStateEvent.KindOn, "smile", Layer, startsActive: true), Off(1d, "smile"));

            player.JumpTo(0d);

            Assert.That(Value(player, 0), Is.EqualTo(1f).Within(1e-5f));
        }

        [Test]
        public void JumpTo_Backward_RecomputesFromStart()
        {
            var player = CreatePlayer(On(0.2d, "smile"), Off(0.5d, "smile"), On(0.6d, "angry"), Off(1d, "angry"));
            player.JumpTo(0.8d);

            player.JumpTo(0.25d);

            Assert.That(Value(player, 0), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(Value(player, 1), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void Advance_PastLastEvent_FinishesTransition()
        {
            var player = CreatePlayer(On(0.2d, "smile"), Off(1d, "smile"));
            player.JumpTo(0.95d);
            player.Advance(0.95d, 1.0d, 0.05f);

            // 終端（Hold）で評価時刻が止まっても、REC 再生と同じく実時間で遷移を進め切る。
            player.Advance(1.0d, 1.0d, 0.2f);

            Assert.That(player.TryWriteValues(new float[BlendShapes.Length]), Is.False, "表情が無く遷移も終われば寄与しない");
        }

        [Test]
        public void JumpTo_ExactlyOnZeroDurationEvent_CompletesTransition()
        {
            var player = CreatePlayer(On(0.2d, "instant"), Off(1d, "instant"));

            player.JumpTo(0.2d);

            Assert.That(Value(player, 2), Is.EqualTo(1f).Within(1e-5f), "遷移時間 0 の表情は、イベントちょうどの時刻で切り替わり終わる");
        }

        [Test]
        public void Advance_HeldToEndOff_IsNotFired()
        {
            var player = CreatePlayer(
                On(0.2d, "smile"),
                new TimelineStateEvent(1d, TimelineStateEvent.KindOff, "smile", Layer, startsActive: false, endsActive: true));
            player.JumpTo(0.9d);

            player.Advance(0.9d, 1.0d + 1e-6d, 0.1f);
            player.Advance(1.0d + 1e-6d, 1.0d + 1e-6d, 0.5f);

            Assert.That(Value(player, 0), Is.EqualTo(1f).Within(1e-5f), "記録の終わりまで押していた表情は離さない");
        }

        [Test]
        public void IsLinearAdvance_MatchesDirectorDelta_IsTrue()
        {
            Assert.That(FacialTrackMixerBehaviour.IsLinearAdvance(1d / 60d, 1d / 60d, 0.5d), Is.True, "Director の dt があればそれで判定する");
            Assert.That(FacialTrackMixerBehaviour.IsLinearAdvance(0.5d, 1d / 60d, 0.5d), Is.False, "Director の dt と合わない前進はシーク");
            Assert.That(FacialTrackMixerBehaviour.IsLinearAdvance(-1d / 60d, 1d / 60d, 1d / 60d), Is.False, "後退はシーク");
        }

        [Test]
        public void IsLinearAdvance_ManualEvaluation_ComparesWithGameFrameDelta()
        {
            Assert.That(FacialTrackMixerBehaviour.IsLinearAdvance(1d / 40d, 0d, 1d / 40d), Is.True, "ゲームのフレームの dt だけ進めた Manual 評価は 1 フレームの前進");
            Assert.That(FacialTrackMixerBehaviour.IsLinearAdvance(1d / 30d, 0d, 1d / 60d), Is.False, "フレームの dt と合わない Manual 評価（スクラブ）はシーク");
            Assert.That(FacialTrackMixerBehaviour.IsLinearAdvance(1d / 60d, 0d, 0d), Is.False);
        }

        private static TimelineExpressionTransitionPlayer CreatePlayer(params TimelineStateEvent[] events)
        {
            var layers = new[] { new LayerDefinition(Layer, 0, ExclusionMode.LastWins) };
            var expressions = new[]
            {
                new Expression("smile", "Smile", Layer, Duration, TransitionCurve.Linear, new[] { new BlendShapeMapping("smile", 1f) }),
                new Expression("angry", "Angry", Layer, Duration, TransitionCurve.Linear, new[] { new BlendShapeMapping("angry", 1f) }),
                new Expression("instant", "Instant", Layer, 0f, TransitionCurve.Linear, new[] { new BlendShapeMapping("instant", 1f) }),
            };
            var profile = new FacialProfile("1.0", layers, expressions);
            return new TimelineExpressionTransitionPlayer(events, BlendShapes, profile, 16, ExclusionMode.LastWins);
        }

        private static TimelineStateEvent On(double time, string expressionId)
        {
            return new TimelineStateEvent(time, TimelineStateEvent.KindOn, expressionId, Layer);
        }

        private static TimelineStateEvent Off(double time, string expressionId)
        {
            return new TimelineStateEvent(time, TimelineStateEvent.KindOff, expressionId, Layer);
        }

        private static float Value(TimelineExpressionTransitionPlayer player, int index)
        {
            var values = new float[BlendShapes.Length];
            player.TryWriteValues(values);
            return values[index];
        }
    }
}
