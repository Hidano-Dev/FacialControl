using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.EditorPreview;
using Hidano.FacialControl.Timeline.Playables;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Expression / Value Mixer の振る舞い。EditMode で graph を評価するため、Play 相当のケースは
    /// <see cref="FacialTimelinePlayMode.OverrideIsPlaying"/> で Play 判定を固定する。
    /// </summary>
    [MediumTest]
    public sealed class FacialTrackMixerBehaviourTests : SizedTestFixture
    {
        private FacialTimelineEditorPreviewBridge.ApplyPreviewDelegate _previousPreview;
        private int _previewCalls;

        [SetUp]
        public void SetUp()
        {
            FacialTimelinePlayMode.OverrideIsPlaying = true;
            _previousPreview = FacialTimelineEditorPreviewBridge.ApplyPreview;
            _previewCalls = 0;
        }

        [TearDown]
        public void TearDown()
        {
            FacialTimelinePlayMode.OverrideIsPlaying = null;
            FacialTimelineEditorPreviewBridge.ApplyPreview = _previousPreview;
        }

        // ================================================================
        // Edit 相当（プレビュー bridge のみ。セッションを開始しない）
        // ================================================================

        [Test]
        public void ExpressionMixer_EditEquivalent_CallsPreviewOnlyAndKeepsSessionIdleWithoutLog()
        {
            FacialTimelinePlayMode.OverrideIsPlaying = false;
            FacialTimelineEditorPreviewBridge.ApplyPreview = (receiver, timeline, time) => _previewCalls++;
            // 接続コンテキスト無しでも Edit ではセッションに触れない。
            using TimelineReceiverTestHost host = CreateHost(CreateTimeline, attach: false);
            using var logs = new TimelineLogCounter();

            host.Director.RebuildGraph();
            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(_previewCalls, Is.GreaterThan(0), "Edit ではプレビュー bridge を呼ぶ");
            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(host.Receiver.Diagnostics.Items, Is.Empty, "Edit 相当の呼び出しは診断状態を変えない");
            Assert.That(logs.Errors + logs.Warnings, Is.EqualTo(0), "Edit 相当の呼び出しで Console に出さない");
        }

        [Test]
        public void ValueMixer_EditEquivalent_CallsPreviewOnlyAndDoesNotTakeOverRegistry()
        {
            FacialTimelinePlayMode.OverrideIsPlaying = false;
            FacialTimelineEditorPreviewBridge.ApplyPreview = (receiver, timeline, time) => _previewCalls++;
            using TimelineReceiverTestHost host = CreateHost(CreateAnalogTimeline);
            var original = new TestAnalogSource("osc:analog", 3);
            host.Registry.Register(AdapterSlug.Parse("osc"), "analog", original);
            using var logs = new TimelineLogCounter();

            host.Director.RebuildGraph();
            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(_previewCalls, Is.GreaterThan(0));
            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(host.Registry.TryResolve("osc:analog", out IInputSource current), Is.True);
            Assert.That(current, Is.SameAs(original), "Edit では乗っ取らない");
            Assert.That(logs.Errors + logs.Warnings, Is.EqualTo(0));
        }

        [Test]
        public void ExpressionMixer_PlayWithFailedSession_DoesNotWriteSinks()
        {
            using TimelineReceiverTestHost host = CreateHost(CreateTimeline);
            // 親トラックが 2 本ある Timeline はセッションを開始しない（MultipleCharacterTracks）。
            ((TimelineAsset)host.Director.playableAsset).CreateTrack<FacialCharacterTrack>(null, "Second Character");
            using var logs = new TimelineLogCounter();

            host.Director.RebuildGraph();
            host.Director.time = 0.25d;
            host.Director.Evaluate();
            host.Director.time = 0.5d;
            host.Director.Evaluate();

            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(host.Receiver.TryGetExpressionSink("Expressions", out _), Is.False);
            Assert.That(logs.Errors, Is.EqualTo(1), "Mixer 自身はログを出さず、Receiver の最初の Error 1 件のみ");
        }

        // ================================================================
        // 親トラック（Facial Character）からの対象解決（HID-408）
        // ================================================================

        [Test]
        public void Mixers_TwoChildTracksUnderCharacter_EachChildMixerRunsOncePerEvaluate()
        {
            // 親トラックの Playable は Passthrough。子トラックの出力はそれぞれ自分の Mixer だけを辿り、
            // 親トラックがまとめて繋ぐ子孫の Clip は何もしない（子の Mixer が二重に動かない）。
            FacialTimelinePlayMode.OverrideIsPlaying = false;
            var calls = new System.Collections.Generic.List<FacialTimelineReceiver>();
            FacialTimelineEditorPreviewBridge.ApplyPreview = (receiver, timeline, time) => calls.Add(receiver);
            using TimelineReceiverTestHost host = CreateHost(timeline => CreateAnalogTimeline(CreateTimeline(timeline)));

            host.Director.RebuildGraph();
            calls.Clear();
            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(calls, Has.Count.EqualTo(2), "表情トラック（Lane 込みで 1 つの Mixer）と値トラックの Mixer が 1 回ずつ");
            Assert.That(calls, Has.All.SameAs(host.Receiver), "どちらも親トラックに Bind した controller の Receiver を使う");
        }

        [Test]
        public void Mixers_CharacterTrackNotBound_DoNotPlayOrStartSession()
        {
            using TimelineReceiverTestHost host = CreateHost(CreateTimeline);
            host.Director.ClearGenericBinding(TimelineReceiverTestHost.Character((TimelineAsset)host.Director.playableAsset));

            host.Director.RebuildGraph();
            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle), "Bind されていない親トラックの子は再生しない");
            Assert.That(host.Receiver.TryGetExpressionSink("Expressions", out _), Is.False);
        }

        [Test]
        public void Mixers_TrackOutsideCharacterBoundToReceiver_IsNotPlayed()
        {
            FacialTimelinePlayMode.OverrideIsPlaying = false;
            FacialTimelineEditorPreviewBridge.ApplyPreview = (receiver, timeline, time) => _previewCalls++;
            using TimelineReceiverTestHost host = CreateHost(timeline =>
            {
                FacialExpressionTrack legacy = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                TimelineClip clip = legacy.CreateClip<FacialExpressionClip>();
                clip.duration = 1d;
                ((FacialExpressionClip)clip.asset).ExpressionId = "smile";
                return timeline;
            });
            foreach (TrackAsset root in ((TimelineAsset)host.Director.playableAsset).GetRootTracks())
            {
                host.Director.SetGenericBinding(root, host.Receiver);
            }

            host.Director.RebuildGraph();
            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(_previewCalls, Is.EqualTo(0), "旧構造（親トラックの外）のトラックは Receiver を Bind していても再生しない");
        }

        // ================================================================
        // Play 相当
        // ================================================================

        [Test]
        public void ExpressionMixer_CollectsParentAndChildLaneEvents_AndAdvancesLinearly()
        {
            using TimelineReceiverTestHost host = CreateHost(CreateTimeline);

            host.Director.Play();
            host.Director.playableGraph.Evaluate(0f);
            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(host.Receiver.TryGetExpressionSink("Expressions", out TimelineExpressionStateSink expressionSink), Is.True);

            host.Director.playableGraph.Evaluate(0.75f);
            CollectionAssert.AreEqual(new[] { "smile", "angry" }, expressionSink.ActiveExpressionIds);

            host.Director.playableGraph.Evaluate(0.5f);
            CollectionAssert.AreEqual(new[] { "angry" }, expressionSink.ActiveExpressionIds);
        }

        [Test]
        public void ExpressionMixer_WhenScrubbedBackward_ReconstructsTargetStackByJump()
        {
            using TimelineReceiverTestHost host = CreateHost(CreateTimeline);

            host.Director.Play();
            host.Director.playableGraph.Evaluate(0f);
            Assert.That(host.Receiver.TryGetExpressionSink("Expressions", out TimelineExpressionStateSink expressionSink), Is.True);

            host.Director.playableGraph.Evaluate(1.25f);
            CollectionAssert.AreEqual(new[] { "angry" }, expressionSink.ActiveExpressionIds);

            host.Director.time = 0.25d;
            host.Director.Evaluate();
            CollectionAssert.AreEqual(new[] { "smile" }, expressionSink.ActiveExpressionIds);
        }

        [Test]
        public void ValueMixer_SamplesAnalogCurvesIntoTakenOverSink()
        {
            using TimelineReceiverTestHost host = CreateHost(CreateAnalogTimeline);
            host.Registry.Register(AdapterSlug.Parse("osc"), "analog", new TestAnalogSource("osc:analog", 3));
            host.Director.RebuildGraph();

            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(host.Receiver.TryGetAnalogSink("osc:analog", out TimelineAnalogInputSource analogSink), Is.True);
            Span<float> axes = stackalloc float[3];
            Assert.That(analogSink.IsValid, Is.True);
            Assert.That(analogSink.TryReadAxes(axes), Is.True);
            Assert.That(axes.ToArray(), Is.EqualTo(new[] { 0.25f, 0.5f, -0.25f }).Within(0.0001f));
        }

        [Test]
        public void ValueMixer_SamplesGazeCurvesAndInvalidatesOutsideClip()
        {
            using TimelineReceiverTestHost host = CreateHost(CreateGazeTimeline);
            host.Registry.Register(AdapterSlug.Parse("osc"), "gaze", new TestAnalogSource("osc:gaze", 2));
            host.Director.RebuildGraph();

            host.Director.time = 0.25d;
            host.Director.Evaluate();

            Assert.That(host.Receiver.TryGetGazeSink("osc:gaze", out TimelineGazeInputSource gazeSink), Is.True);
            Assert.That(gazeSink.IsValid, Is.True);
            Assert.That(gazeSink.TryReadVector2(out float x, out float y), Is.True);
            Assert.That(x, Is.EqualTo(-0.5f).Within(0.0001f));
            Assert.That(y, Is.EqualTo(0.5f).Within(0.0001f));

            host.Director.time = 0.75d;
            host.Director.Evaluate();

            Assert.That(gazeSink.IsValid, Is.False);
            Assert.That(gazeSink.TryReadVector2(out _, out _), Is.False);
        }

        /// <summary>
        /// 初期化済み FacialController + Receiver + Director を組み、全 Facial トラックへ同じ Bake 参照を書いて
        /// Director にバインドし、接続コンテキストを Receiver に渡す。
        /// </summary>
        private static TimelineReceiverTestHost CreateHost(Func<TimelineAsset, TimelineAsset> build, bool attach = true)
        {
            TimelineReceiverTestHost host = TimelineReceiverTestHost.Create(CreateProfile(), new[] { "Smile", "Angry" });
            TimelineAsset timeline = build(host.CreateTimeline());
            FacialTimelineBakeAsset bake = host.CreateBake(timeline);
            TimelineReceiverTestHost.AssignBakeToAllTracks(timeline, bake);
            host.StampHashes(timeline, bake);
            host.BindDirector(timeline);
            if (attach)
            {
                host.Attach();
            }

            return host;
        }

        private static TimelineAsset CreateTimeline(TimelineAsset timeline)
        {
            var parentTrack = TimelineReceiverTestHost.Character(timeline).CreateChildTrack<FacialExpressionTrack>("Expressions");
            TimelineClip parentClip = parentTrack.CreateClip<FacialExpressionClip>();
            parentClip.start = 0.0d;
            parentClip.duration = 1.0d;
            ((FacialExpressionClip)parentClip.asset).ExpressionId = "smile";

            var childTrack = timeline.CreateTrack<FacialExpressionTrack>(parentTrack, "Expressions Layer");
            TimelineClip childClip = childTrack.CreateClip<FacialExpressionClip>();
            childClip.start = 0.5d;
            childClip.duration = 1.0d;
            ((FacialExpressionClip)childClip.asset).ExpressionId = "angry";

            return timeline;
        }

        private static TimelineAsset CreateAnalogTimeline(TimelineAsset timeline)
        {
            var track = TimelineReceiverTestHost.Character(timeline).CreateChildTrack<FacialValueTrack>("Analog");
            track.ChannelSubId = "osc:analog";
            track.ChannelKind = FacialValueChannelKind.Analog;

            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0.0d;
            clip.duration = 0.5d;
            ((FacialValueClip)clip.asset).Axes = new[]
            {
                AnimationCurve.Linear(0f, 0f, 0.5f, 0.5f),
                AnimationCurve.Linear(0f, 1f, 0.5f, 0f),
                AnimationCurve.Linear(0f, -0.5f, 0.5f, 0f),
            };

            return timeline;
        }

        private static TimelineAsset CreateGazeTimeline(TimelineAsset timeline)
        {
            var track = TimelineReceiverTestHost.Character(timeline).CreateChildTrack<FacialValueTrack>("Gaze");
            track.ChannelSubId = "osc:gaze";
            track.ChannelKind = FacialValueChannelKind.Gaze;

            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0.0d;
            clip.duration = 0.5d;
            ((FacialValueClip)clip.asset).Axes = new[]
            {
                AnimationCurve.Linear(0f, -1f, 0.5f, 0f),
                AnimationCurve.Linear(0f, 1f, 0.5f, 0f),
            };

            TimelineClip secondClip = track.CreateClip<FacialValueClip>();
            secondClip.start = 1.0d;
            secondClip.duration = 0.25d;
            ((FacialValueClip)secondClip.asset).Axes = new[]
            {
                AnimationCurve.Linear(0f, 0f, 0.25f, 0.5f),
                AnimationCurve.Linear(0f, 0f, 0.25f, -0.5f),
            };

            return timeline;
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("Expressions", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    CreateExpression("smile", "Smile"),
                    CreateExpression("angry", "Angry"),
                });
        }

        private static Expression CreateExpression(string id, string name)
        {
            return new Expression(
                id: id,
                name: name,
                layer: "Expressions",
                transitionDuration: 0.1f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping(name, 1.0f),
                });
        }
    }
}
