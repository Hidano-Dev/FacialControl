using Hidano.FacialControl.Adapters.ScriptableObject;
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

        [Test]
        public void ComputeProfileContentHash_SameInput_ReturnsSameValueAndHex()
        {
            ulong first = FacialTimelineHashCalculator.ComputeProfileContentHash(CreateProfile(0.10f), CreateGazeChannels("osc:gaze"));
            ulong second = FacialTimelineHashCalculator.ComputeProfileContentHash(CreateProfile(0.10f), CreateGazeChannels("osc:gaze"));

            Assert.That(first, Is.EqualTo(second));
            Assert.That(
                FacialTimelineHashCalculator.ComputeProfileContentHashHex(CreateProfile(0.10f), CreateGazeChannels("osc:gaze")),
                Is.EqualTo(first.ToString("x16")));
        }

        [Test]
        public void ComputeProfileContentHash_WhenLayersChange_ReturnsDifferentValue()
        {
            var original = CreateProfile(0.10f, layerPriority: 0);
            var changed = CreateProfile(0.10f, layerPriority: 1);

            Assert.That(
                FacialTimelineHashCalculator.ComputeProfileContentHash(original, NoGaze),
                Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(changed, NoGaze)));
        }

        [Test]
        public void ComputeProfileContentHash_WhenLayerInputSourcesChange_ReturnsDifferentValue()
        {
            var original = CreateProfile(0.10f, inputSourceWeight: 1.0f);
            var changedWeight = CreateProfile(0.10f, inputSourceWeight: 0.5f);
            var changedId = CreateProfile(0.10f, inputSourceWeight: 1.0f, inputSourceId: "timeline:emotion");

            ulong originalHash = FacialTimelineHashCalculator.ComputeProfileContentHash(original, NoGaze);

            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(changedWeight, NoGaze)));
            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(changedId, NoGaze)));
        }

        [Test]
        public void ComputeProfileContentHash_WhenGazeChannelsChange_ReturnsDifferentValue()
        {
            var profile = CreateProfile(0.10f);
            GazeChannel[] original = CreateGazeChannels("osc:gaze");
            GazeChannel[] changedSource = CreateGazeChannels("osc:gaze.left");
            GazeChannel[] changedBone = CreateGazeChannels("osc:gaze");
            changedBone[0].leftEyeBonePath = "Armature/Head/LeftEye";

            ulong originalHash = FacialTimelineHashCalculator.ComputeProfileContentHash(profile, original);

            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(profile, changedSource)));
            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(profile, changedBone)));
            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(profile, NoGaze)));
        }

        [Test]
        public void ComputeProfileContentHash_WhenExpressionsChange_ReturnsDifferentValue()
        {
            var original = CreateProfile(0.10f);
            var changedTransition = CreateProfile(0.40f);
            var withExtraExpression = CreateProfile(0.10f, includeAngry: true);

            ulong originalHash = FacialTimelineHashCalculator.ComputeProfileContentHash(original, NoGaze);

            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(changedTransition, NoGaze)));
            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(withExtraExpression, NoGaze)));
        }

        [Test]
        public void ComputeProfileContentHash_ExpressionOrder_DoesNotAffectValue()
        {
            var smileFirst = CreateProfile(0.10f, includeAngry: true, angryFirst: false);
            var angryFirst = CreateProfile(0.10f, includeAngry: true, angryFirst: true);

            Assert.That(
                FacialTimelineHashCalculator.ComputeProfileContentHash(smileFirst, NoGaze),
                Is.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(angryFirst, NoGaze)));
        }

        [Test]
        public void ComputeProfileContentHash_WhenSlotsDefaultOverlaysOrBaseExpressionChange_ReturnsDifferentValue()
        {
            var original = CreateProfile(0.10f);
            var withSlots = CreateProfile(0.10f, slots: new[] { "blink" });
            var withDefaultOverlay = CreateProfile(
                0.10f,
                slots: new[] { "blink" },
                defaultOverlays: new[] { new OverlaySlotBinding("blink", true, null) });
            var withBaseExpression = CreateProfile(
                0.10f,
                baseExpression: new[] { new BlendShapeSnapshot("Face", "Smile", 0.2f) });

            ulong originalHash = FacialTimelineHashCalculator.ComputeProfileContentHash(original, NoGaze);
            ulong slotsHash = FacialTimelineHashCalculator.ComputeProfileContentHash(withSlots, NoGaze);

            Assert.That(originalHash, Is.Not.EqualTo(slotsHash));
            Assert.That(slotsHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(withDefaultOverlay, NoGaze)));
            Assert.That(originalHash, Is.Not.EqualTo(FacialTimelineHashCalculator.ComputeProfileContentHash(withBaseExpression, NoGaze)));
        }

        [Test]
        public void ComputeProfileContentHash_WhenOnlyTimelineChanges_IsUnchangedWhileSourceHashChanges()
        {
            var profile = CreateProfile(0.10f);
            GazeChannel[] gaze = CreateGazeChannels("osc:gaze");
            var original = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);
            var moved = CreateTimeline("Expressions", 0.5d, 1.0d, 0.25f);

            try
            {
                ulong profileHashBefore = FacialTimelineHashCalculator.ComputeProfileContentHash(profile, gaze);
                ulong sourceHashBefore = FacialTimelineHashCalculator.ComputeHash(original, profile, gaze);
                ulong sourceHashAfter = FacialTimelineHashCalculator.ComputeHash(moved, profile, gaze);
                ulong profileHashAfter = FacialTimelineHashCalculator.ComputeProfileContentHash(profile, gaze);

                Assert.That(sourceHashAfter, Is.Not.EqualTo(sourceHashBefore));
                Assert.That(profileHashAfter, Is.EqualTo(profileHashBefore));
            }
            finally
            {
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(moved);
            }
        }

        [Test]
        public void ComputeHash_IncludesProfileContentHash_GazeChannelChangeChangesSourceHash()
        {
            var profile = CreateProfile(0.10f);
            var timeline = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);

            try
            {
                ulong withGaze = FacialTimelineHashCalculator.ComputeHash(timeline, profile, CreateGazeChannels("osc:gaze"));
                ulong withOtherGaze = FacialTimelineHashCalculator.ComputeHash(timeline, profile, CreateGazeChannels("osc:gaze.left"));

                Assert.That(withGaze, Is.Not.EqualTo(withOtherGaze));
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, CreateGazeChannels("osc:gaze")),
                    Is.EqualTo(withGaze.ToString("x16")));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ComputeHash_WithoutGazeChannels_EqualsEmptyGazeChannelOverload()
        {
            var profile = CreateProfile(0.10f);
            var timeline = CreateTimeline("Expressions", 0.0d, 1.0d, 0.25f);

            try
            {
                Assert.That(
                    FacialTimelineHashCalculator.ComputeHash(timeline, profile),
                    Is.EqualTo(FacialTimelineHashCalculator.ComputeHash(timeline, profile, NoGaze)));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        private static readonly GazeChannel[] NoGaze = new GazeChannel[0];

        private static GazeChannel[] CreateGazeChannels(string sourceIdLeft)
        {
            return new[]
            {
                new GazeChannel
                {
                    id = "gaze",
                    sourceIdLeft = sourceIdLeft,
                    sourceIdRight = sourceIdLeft,
                },
            };
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

        private static FacialProfile CreateProfile(
            float transitionDuration,
            int layerPriority = 0,
            float inputSourceWeight = 1.0f,
            string inputSourceId = "osc:analog-expression",
            bool includeAngry = false,
            bool angryFirst = false,
            string[] slots = null,
            OverlaySlotBinding[] defaultOverlays = null,
            BlendShapeSnapshot[] baseExpression = null)
        {
            var smile = new Expression(
                id: "smile",
                name: "Smile",
                layer: "emotion",
                transitionDuration: transitionDuration,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping("Smile", 1.0f, "Face"),
                    new BlendShapeMapping("Blink", 0.25f, "Face"),
                });
            var angry = new Expression(
                id: "angry",
                name: "Angry",
                layer: "emotion",
                transitionDuration: 0.2f,
                transitionCurve: TransitionCurve.Linear,
                blendShapeValues: new[]
                {
                    new BlendShapeMapping("Angry", 1.0f, "Face"),
                });

            Expression[] expressions;
            if (!includeAngry)
            {
                expressions = new[] { smile };
            }
            else
            {
                expressions = angryFirst ? new[] { angry, smile } : new[] { smile, angry };
            }

            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("emotion", layerPriority, ExclusionMode.LastWins),
                },
                expressions: expressions,
                layerInputSources: new[]
                {
                    new[] { new InputSourceDeclaration(inputSourceId, inputSourceWeight, null) },
                },
                defaultOverlays: defaultOverlays,
                slots: slots,
                baseExpression: baseExpression);
        }
    }
}
