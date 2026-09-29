using System;
using System.Collections.Generic;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Timeline;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Tracks;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineBakeServiceTests : SizedTestFixture
    {
        [Test]
        public void Bake_WhenClipBoundariesAreOffGrid_ContainsKeysAtExactEventTimes()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            try
            {
                FacialProfile profile = CreateProfile(transitionDuration: 0.25f);
                FacialExpressionTrack track = CreateExpressionTrack(timeline, start: 0.025d, duration: 0.25d);

                var bake = Editor.TimelineBakeService.Bake(timeline, profile);
                BlendShapeCurve curve = FindCurve(bake, track.name, "Smile");

                Assert.That(ContainsKeyAtTime(curve.Curve, 0.025f), Is.True);
                Assert.That(ContainsKeyAtTime(curve.Curve, 0.275f), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Bake_WithLinearTransition_MatchesLiveTransitionAtArbitrarySampleTimes()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialTimelineBakeAsset bake = null;
            try
            {
                const float transitionDuration = 0.25f;
                FacialProfile profile = CreateProfile(transitionDuration);
                CreateExpressionTrack(timeline, start: 0.025d, duration: 0.50d);

                bake = Editor.TimelineBakeService.Bake(timeline, profile);
                BlendShapeCurve curve = FindCurve(bake, "Expressions", "Smile");

                float[] sampleTimes =
                {
                    0.025f,
                    0.0833333f,
                    0.1500000f,
                    0.2416667f,
                    0.3000000f,
                    0.4416667f,
                    0.5250000f,
                    0.6083333f,
                    0.7083333f,
                    0.7750000f,
                };

                for (int i = 0; i < sampleTimes.Length; i++)
                {
                    float time = sampleTimes[i];
                    float expected = SimulateLiveLinearValue(profile, time, transitionDuration);
                    float actual = curve.Curve.Evaluate(time);
                    Assert.That(actual, Is.EqualTo(expected).Within(0.0001f), $"t={time}");
                }
            }
            finally
            {
                if (bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(bake);
                }

                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Bake_IncludesStateEventsAndMergedValueChannels()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialTimelineBakeAsset bake = null;

            try
            {
                FacialProfile profile = CreateProfile(transitionDuration: 0.25f);
                FacialExpressionTrack expressionTrack = CreateExpressionTrack(timeline, start: 0.025d, duration: 0.50d);
                expressionTrack.name = "Expressions";

                FacialValueTrack analogTrack = timeline.CreateTrack<FacialValueTrack>(null, "Analog");
                analogTrack.ChannelSubId = "analog-main";
                analogTrack.ChannelKind = FacialValueChannelKind.Analog;
                TimelineClip analogClip = analogTrack.CreateClip<FacialValueClip>();
                analogClip.start = 0.10d;
                analogClip.duration = 0.30d;
                ((FacialValueClip)analogClip.asset).Axes = new[]
                {
                    AnimationCurve.Linear(0f, 0f, 0.30f, 0.6f),
                    AnimationCurve.Linear(0f, 1f, 0.30f, -1f),
                };

                FacialValueTrack gazeTrack = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
                gazeTrack.ChannelSubId = "gaze-main";
                gazeTrack.ChannelKind = FacialValueChannelKind.Gaze;
                TimelineClip gazeClip = gazeTrack.CreateClip<FacialValueClip>();
                gazeClip.start = 0.20d;
                gazeClip.duration = 0.25d;
                ((FacialValueClip)gazeClip.asset).Axes = new[]
                {
                    AnimationCurve.Linear(0f, -1f, 0.25f, 1f),
                    AnimationCurve.Linear(0f, 1f, 0.25f, -1f),
                };

                bake = Editor.TimelineBakeService.Bake(timeline, profile);

                Assert.That(bake.StateEvents, Has.Length.EqualTo(2));
                Assert.That(bake.StateEvents[0].TimeSeconds, Is.EqualTo(0.025d).Within(1e-9d));
                Assert.That(bake.StateEvents[0].ExpressionId, Is.EqualTo("smile"));
                Assert.That(bake.StateEvents[1].TimeSeconds, Is.EqualTo(0.525d).Within(1e-9d));
                Assert.That(bake.StateEvents[1].Kind, Is.EqualTo(TimelineStateEvent.KindOff));

                Assert.That(bake.ValueBakes, Has.Length.EqualTo(2));

                ValueChannelBake analogBake = FindValueBake(bake, "analog-main");
                Assert.That(analogBake.IsGaze, Is.False);
                Assert.That(analogBake.Axes, Has.Length.EqualTo(2));
                Assert.That(analogBake.Axes[0].Evaluate(0.05f), Is.EqualTo(0f).Within(0.0001f));
                Assert.That(analogBake.Axes[0].Evaluate(0.25f), Is.EqualTo(0.3f).Within(0.02f));
                Assert.That(analogBake.Axes[0].Evaluate(0.45f), Is.EqualTo(0f).Within(0.0001f));

                ValueChannelBake gazeBake = FindValueBake(bake, "gaze-main");
                Assert.That(gazeBake.IsGaze, Is.True);
                Assert.That(gazeBake.Axes, Has.Length.EqualTo(2));
                Assert.That(gazeBake.Axes[0].Evaluate(0.20f), Is.EqualTo(-1f).Within(0.0001f));
                Assert.That(gazeBake.Axes[0].Evaluate(0.325f), Is.EqualTo(0f).Within(0.02f));
                Assert.That(gazeBake.Axes[0].Evaluate(0.46f), Is.EqualTo(0f).Within(0.0001f));
            }
            finally
            {
                if (bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(bake);
                }

                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Bake_SameInputTwice_ProducesDeterministicBakeData()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialTimelineBakeAsset firstBake = null;
            FacialTimelineBakeAsset secondBake = null;

            try
            {
                FacialProfile profile = CreateProfile(transitionDuration: 0.25f);
                CreateExpressionTrack(timeline, start: 0.025d, duration: 0.50d);

                FacialValueTrack analogTrack = timeline.CreateTrack<FacialValueTrack>(null, "Analog");
                analogTrack.ChannelSubId = "analog-main";
                analogTrack.ChannelKind = FacialValueChannelKind.Analog;
                TimelineClip analogClip = analogTrack.CreateClip<FacialValueClip>();
                analogClip.start = 0.10d;
                analogClip.duration = 0.20d;
                ((FacialValueClip)analogClip.asset).Axes = new[]
                {
                    AnimationCurve.Linear(0f, 0f, 0.20f, 1f),
                };

                firstBake = Editor.TimelineBakeService.Bake(timeline, profile);
                secondBake = Editor.TimelineBakeService.Bake(timeline, profile);

                Assert.That(secondBake.SourceHashHex, Is.EqualTo(firstBake.SourceHashHex));
                Assert.That(secondBake.StateEvents, Has.Length.EqualTo(firstBake.StateEvents.Length));
                Assert.That(secondBake.ValueBakes, Has.Length.EqualTo(firstBake.ValueBakes.Length));

                BlendShapeCurve firstCurve = FindCurve(firstBake, "Expressions", "Smile");
                BlendShapeCurve secondCurve = FindCurve(secondBake, "Expressions", "Smile");
                Assert.That(secondCurve.Curve.keys, Is.EqualTo(firstCurve.Curve.keys));

                ValueChannelBake firstValueBake = FindValueBake(firstBake, "analog-main");
                ValueChannelBake secondValueBake = FindValueBake(secondBake, "analog-main");
                Assert.That(secondValueBake.Axes[0].keys, Is.EqualTo(firstValueBake.Axes[0].keys));
            }
            finally
            {
                if (firstBake != null)
                {
                    UnityEngine.Object.DestroyImmediate(firstBake);
                }

                if (secondBake != null)
                {
                    UnityEngine.Object.DestroyImmediate(secondBake);
                }

                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void IsStale_WhenValueTrackChanges_ReturnsTrue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            FacialTimelineBakeAsset bake = null;

            try
            {
                FacialProfile profile = CreateProfile(transitionDuration: 0.25f);
                CreateExpressionTrack(timeline, start: 0.025d, duration: 0.50d);

                FacialValueTrack valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "Analog");
                valueTrack.ChannelSubId = "analog-main";
                valueTrack.ChannelKind = FacialValueChannelKind.Analog;
                TimelineClip clip = valueTrack.CreateClip<FacialValueClip>();
                clip.start = 0.0d;
                clip.duration = 0.25d;
                ((FacialValueClip)clip.asset).Axes = new[]
                {
                    AnimationCurve.Linear(0f, 0f, 0.25f, 0.5f),
                };

                bake = Editor.TimelineBakeService.Bake(timeline, profile);
                Assert.That(Editor.TimelineBakeService.IsStale(timeline, profile, bake), Is.False);

                ((FacialValueClip)clip.asset).Axes = new[]
                {
                    AnimationCurve.Linear(0f, 0f, 0.25f, 0.75f),
                };

                Assert.That(Editor.TimelineBakeService.IsStale(timeline, profile, bake), Is.True);
            }
            finally
            {
                if (bake != null)
                {
                    UnityEngine.Object.DestroyImmediate(bake);
                }

                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        private static float SimulateLiveLinearValue(FacialProfile profile, float time, float transitionDuration)
        {
            string[] blendShapeNames = { "Smile" };
            var source = new TestExpressionSource(
                InputSourceId.Parse("timeline:test"),
                blendShapeNames,
                profile,
                maxStackDepth: 4,
                exclusionMode: ExclusionMode.LastWins);

            if (time >= 0.025f)
            {
                source.TriggerOn("smile");
                source.Tick(Mathf.Min(time - 0.025f, transitionDuration));
            }

            if (time >= 0.525f)
            {
                source.TriggerOff("smile");
                source.Tick(Mathf.Min(time - 0.525f, transitionDuration));
            }
            else if (time > 0.275f)
            {
                source.Tick(time - 0.275f);
            }

            Span<float> values = stackalloc float[1];
            return source.TryWriteValues(values) ? values[0] : 0f;
        }

        private static FacialProfile CreateProfile(float transitionDuration)
        {
            return new FacialProfile(
                schemaVersion: "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("Expressions", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "Expressions",
                        transitionDuration: transitionDuration,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }

        private static FacialExpressionTrack CreateExpressionTrack(TimelineAsset timeline, double start, double duration)
        {
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = start;
            clip.duration = duration;
            ((FacialExpressionClip)clip.asset).ExpressionId = "smile";
            return track;
        }

        private static BlendShapeCurve FindCurve(FacialTimelineBakeAsset bake, string layerName, string blendShapeName)
        {
            Assert.That(bake, Is.Not.Null);
            Assert.That(bake.ExpressionBakes, Is.Not.Null);

            for (int bakeIndex = 0; bakeIndex < bake.ExpressionBakes.Length; bakeIndex++)
            {
                ExpressionSourceBake expressionBake = bake.ExpressionBakes[bakeIndex];
                if (!string.Equals(expressionBake.LayerName, layerName, StringComparison.Ordinal))
                {
                    continue;
                }

                BlendShapeCurve[] curves = expressionBake.Curves ?? Array.Empty<BlendShapeCurve>();
                for (int curveIndex = 0; curveIndex < curves.Length; curveIndex++)
                {
                    if (string.Equals(curves[curveIndex].BlendShapeName, blendShapeName, StringComparison.Ordinal))
                    {
                        return curves[curveIndex];
                    }
                }
            }

            Assert.Fail($"Curve not found. layer={layerName}, blendShape={blendShapeName}");
            return null;
        }

        private static ValueChannelBake FindValueBake(FacialTimelineBakeAsset bake, string sub)
        {
            Assert.That(bake, Is.Not.Null);
            Assert.That(bake.ValueBakes, Is.Not.Null);

            for (int i = 0; i < bake.ValueBakes.Length; i++)
            {
                if (string.Equals(bake.ValueBakes[i].Sub, sub, StringComparison.Ordinal))
                {
                    return bake.ValueBakes[i];
                }
            }

            Assert.Fail($"Value bake not found. sub={sub}");
            return null;
        }

        private static bool ContainsKeyAtTime(AnimationCurve curve, float time)
        {
            Keyframe[] keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                if (Mathf.Abs(keys[i].time - time) <= 0.0001f)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class TestExpressionSource : ExpressionTriggerInputSourceBase
        {
            public TestExpressionSource(
                InputSourceId id,
                IReadOnlyList<string> blendShapeNames,
                FacialProfile profile,
                int maxStackDepth,
                ExclusionMode exclusionMode)
                : base(
                    id,
                    blendShapeCount: blendShapeNames.Count,
                    maxStackDepth: maxStackDepth,
                    exclusionMode: exclusionMode,
                    blendShapeNames: blendShapeNames,
                    profile: profile)
            {
            }
        }
    }
}
