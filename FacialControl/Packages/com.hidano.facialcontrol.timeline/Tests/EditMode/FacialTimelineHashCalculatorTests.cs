using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class FacialTimelineHashCalculatorTests : SizedTestFixture
    {
        [Test]
        public void ComputeHash_SameSource_ReturnsSameValue()
        {
            var profile = CreateProfile(0.10f);
            var timelineA = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);
            var timelineB = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);

            try
            {
                ulong hashA = FacialTimelineHashCalculator.ComputeHash(timelineA, profile);
                ulong hashB = FacialTimelineHashCalculator.ComputeHash(timelineB, profile);

                Assert.That(hashA, Is.EqualTo(hashB));
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHashHex(timelineA, profile),
                    Is.EqualTo(hashA.ToString("x16")));
            }
            finally
            {
                Object.DestroyImmediate(timelineA);
                Object.DestroyImmediate(timelineB);
            }
        }

        [Test]
        public void ComputeHash_WhenClipTimingChanges_ReturnsDifferentValue()
        {
            var profile = CreateProfile(0.10f);
            var original = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);
            var moved = CreateTimeline("Expressions", 0.5d, 1.0d, 0.25f);

            try
            {
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHash(original, profile),
                    Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeHash(moved, profile)));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(moved);
            }
        }

        [Test]
        public void ComputeHash_WhenKeyframeChanges_ReturnsDifferentValue()
        {
            var profile = CreateProfile(0.10f);
            var original = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);
            var changed = CreateTimeline("Expressions", 0.0d, 1.0d, 0.75f);

            try
            {
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHash(original, profile),
                    Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeHash(changed, profile)));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(changed);
            }
        }

        [Test]
        public void ComputeHash_WhenProfileTransitionDurationChanges_ReturnsDifferentValue()
        {
            var timeline = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);

            try
            {
                ulong shortTransition = FacialTimelineHashCalculator.ComputeHash(timeline, CreateProfile(0.10f));
                ulong longTransition = FacialTimelineHashCalculator.ComputeHash(timeline, CreateProfile(0.40f));

                Assert.That(shortTransition, Is.Not.EqualTo(longTransition));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ComputeHash_WhenTrackOrderChanges_ReturnsDifferentValue()
        {
            var profile = CreateProfile(0.10f);
            var firstOrder = CreateTimelineWithTwoValueTracks("alpha", "beta");
            var secondOrder = CreateTimelineWithTwoValueTracks("beta", "alpha");

            try
            {
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHash(firstOrder, profile),
                    Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeHash(secondOrder, profile)));
            }
            finally
            {
                Object.DestroyImmediate(firstOrder);
                Object.DestroyImmediate(secondOrder);
            }
        }

        [Test]
        public void ComputeHash_WhenTrackIsRenamed_ReturnsDifferentValue()
        {
            var profile = CreateProfile(0.10f);
            var original = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);
            var renamed = CreateTimeline("Expressions Renamed", 0.0d, 1.0d, 0.25f);

            try
            {
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHash(original, profile),
                    Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeHash(renamed, profile)));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(renamed);
            }
        }

        private static TimelineAsset CreateTimeline(
            string trackName,
            double clipStart,
            double clipDuration,
            float curvePeak)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            var expressionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, trackName);
            TimelineClip expressionClip = expressionTrack.CreateClip<FacialExpressionClip>();
            expressionClip.start = clipStart;
            expressionClip.duration = clipDuration;
            ((FacialExpressionClip)expressionClip.asset).ExpressionId = "smile";

            var valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
            valueTrack.ChannelSubId = "gaze-main";
            valueTrack.ChannelKind = FacialValueChannelKind.Gaze;

            TimelineClip valueClip = valueTrack.CreateClip<FacialValueClip>();
            valueClip.start = clipStart;
            valueClip.duration = clipDuration;
            ((FacialValueClip)valueClip.asset).Axes = new[]
            {
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.5f, curvePeak),
                    new Keyframe(1f, 0f)),
                AnimationCurve.Linear(0f, -1f, 1f, 1f),
            };

            return timeline;
        }

        private static TimelineAsset CreateTimelineWithTwoValueTracks(string firstSubId, string secondSubId)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            CreateValueTrack(timeline, "Track A", firstSubId, 0.25f);
            CreateValueTrack(timeline, "Track B", secondSubId, 0.75f);
            return timeline;
        }

        private static void CreateValueTrack(
            TimelineAsset timeline,
            string trackName,
            string subId,
            float peakValue)
        {
            var track = timeline.CreateTrack<FacialValueTrack>(null, trackName);
            track.ChannelSubId = subId;
            track.ChannelKind = FacialValueChannelKind.Analog;

            TimelineClip clip = track.CreateClip<FacialValueClip>();
            clip.start = 0.0d;
            clip.duration = 1.0d;
            ((FacialValueClip)clip.asset).Axes = new[]
            {
                new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.5f, peakValue),
                    new Keyframe(1f, 1f)),
            };
        }

        private static FacialProfile CreateProfile(float transitionDuration)
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "emotion",
                        transitionDuration: transitionDuration,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1.0f, "Face"),
                            new BlendShapeMapping("Blink", 0.25f, "Face"),
                        }),
                });
        }
    }
}
