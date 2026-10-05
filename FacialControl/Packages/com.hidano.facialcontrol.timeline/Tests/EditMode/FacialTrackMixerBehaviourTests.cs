using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [MediumTest]
    public sealed class FacialTrackMixerBehaviourTests : SizedTestFixture
    {
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
        private static TimelineReceiverTestHost CreateHost(Func<TimelineAsset, TimelineAsset> build)
        {
            TimelineReceiverTestHost host = TimelineReceiverTestHost.Create(CreateProfile(), new[] { "Smile", "Angry" });
            TimelineAsset timeline = build(host.CreateTimeline());
            FacialTimelineBakeAsset bake = host.CreateBake(timeline);
            TimelineReceiverTestHost.AssignBakeToAllTracks(timeline, bake);
            host.StampHashes(timeline, bake);
            host.BindDirector(timeline);
            host.Attach();
            return host;
        }

        private static TimelineAsset CreateTimeline(TimelineAsset timeline)
        {
            var parentTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
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
            var track = timeline.CreateTrack<FacialValueTrack>(null, "Analog");
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
            var track = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
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
