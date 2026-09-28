using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor.TrackEditors;
using Hidano.FacialControl.Timeline.Editor.Validation;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor.Timeline;
using UnityEngine;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class FacialTimelineValidatorTests : SizedTestFixture
    {
        [Test]
        public void Validate_WithChildLaneOnly_ReturnsEmptyParentTrackIssue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialProfile profile = CreateProfile();
                FacialExpressionTrack parentTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                FacialExpressionTrack childTrack = timeline.CreateTrack<FacialExpressionTrack>(parentTrack, "Expressions Lane 1");
                TimelineClip childClip = childTrack.CreateClip<FacialExpressionClip>();
                ((FacialExpressionClip)childClip.asset).ExpressionId = "smile";
                childClip.start = 0.1d;
                childClip.duration = 0.5d;

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, profile);

                Assert.That(report.Issues.Count, Is.EqualTo(1));
                Assert.That(report.Issues[0].Kind, Is.EqualTo(FacialTimelineIssueKind.EmptyParentTrack));
                Assert.That(report.Issues[0].TrackName, Is.EqualTo("Expressions"));
                Assert.That(report.Issues[0].Message, Does.Contain("Move at least one clip back to the parent track"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validate_WithRootClip_DoesNotReturnEmptyParentTrackIssue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialProfile profile = CreateProfile();
                FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                TimelineClip clip = track.CreateClip<FacialExpressionClip>();
                ((FacialExpressionClip)clip.asset).ExpressionId = "smile";
                clip.duration = 0.5d;

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, profile);

                Assert.That(report.Issues, Is.Empty);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validate_WithMissingExpressionId_ReturnsIssue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                TimelineClip clip = track.CreateClip<FacialExpressionClip>();
                ((FacialExpressionClip)clip.asset).ExpressionId = "missing";
                clip.start = 0.25d;
                clip.duration = 0.5d;

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, CreateProfile());

                Assert.That(report.Issues.Count, Is.EqualTo(1));
                Assert.That(report.Issues[0].Kind, Is.EqualTo(FacialTimelineIssueKind.MissingExpressionId));
                Assert.That(report.Issues[0].TimeSeconds, Is.EqualTo(0.25d).Within(1e-9d));
                Assert.That(report.Issues[0].Message, Does.Contain("does not exist"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validate_WithGazeOutOfRange_ReturnsIssue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialValueTrack track = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
                track.ChannelSubId = "gaze-main";
                track.ChannelKind = FacialValueChannelKind.Gaze;
                TimelineClip clip = track.CreateClip<FacialValueClip>();
                clip.start = 0.5d;
                clip.duration = 0.75d;
                ((FacialValueClip)clip.asset).Axes = new[]
                {
                    new AnimationCurve(new Keyframe(0.25f, 1.25f)),
                    new AnimationCurve(new Keyframe(0.25f, 0.5f)),
                };

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, CreateProfile());

                Assert.That(report.Issues.Count, Is.EqualTo(1));
                Assert.That(report.Issues[0].Kind, Is.EqualTo(FacialTimelineIssueKind.GazeOutOfRange));
                Assert.That(report.Issues[0].TimeSeconds, Is.EqualTo(0.75d).Within(1e-9d));
                Assert.That(report.Issues[0].Message, Does.Contain("[-1, 1]"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validate_WithEmptyValueClip_ReturnsIssue()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialValueTrack track = timeline.CreateTrack<FacialValueTrack>(null, "Analog");
                track.ChannelSubId = "analog-main";
                TimelineClip clip = track.CreateClip<FacialValueClip>();
                clip.start = 0.1d;
                clip.duration = 0.5d;
                ((FacialValueClip)clip.asset).Axes = Array.Empty<AnimationCurve>();

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, CreateProfile());

                Assert.That(report.Issues.Count, Is.EqualTo(1));
                Assert.That(report.Issues[0].Kind, Is.EqualTo(FacialTimelineIssueKind.EmptyClip));
                Assert.That(report.Issues[0].Message, Does.Contain("at least one curve"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ExpressionClipEditor_BuildClipOptions_WhenIssueExists_SetsErrorText()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                TimelineClip clip = track.CreateClip<FacialExpressionClip>();
                ((FacialExpressionClip)clip.asset).ExpressionId = "missing";
                clip.duration = 0.5d;

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, CreateProfile());
                ClipDrawOptions options = FacialExpressionClipEditor.BuildClipOptions(clip, new ClipDrawOptions(), report);

                Assert.That(options.errorText, Does.Contain("does not exist"));
                Assert.That(options.tooltip, Does.Contain("does not exist"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ExpressionTrackEditor_BuildTrackOptions_WhenParentTrackEmpty_SetsErrorText()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialExpressionTrack parentTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
                FacialExpressionTrack childTrack = timeline.CreateTrack<FacialExpressionTrack>(parentTrack, "Expressions Lane 1");
                TimelineClip childClip = childTrack.CreateClip<FacialExpressionClip>();
                ((FacialExpressionClip)childClip.asset).ExpressionId = "smile";
                childClip.duration = 0.5d;

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, CreateProfile());
                TrackDrawOptions options = FacialExpressionTrackEditor.BuildTrackOptions(parentTrack, new TrackDrawOptions(), report);

                Assert.That(options.errorText, Does.Contain("Move at least one clip back to the parent track"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ValueClipEditor_BuildClipOptions_WhenGazeOutOfRange_SetsErrorText()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();

            try
            {
                FacialValueTrack track = timeline.CreateTrack<FacialValueTrack>(null, "Gaze");
                track.ChannelSubId = "gaze-main";
                track.ChannelKind = FacialValueChannelKind.Gaze;
                TimelineClip clip = track.CreateClip<FacialValueClip>();
                clip.duration = 0.5d;
                ((FacialValueClip)clip.asset).Axes = new[]
                {
                    new AnimationCurve(new Keyframe(0.1f, -1.1f)),
                    new AnimationCurve(new Keyframe(0.1f, 0.0f)),
                };

                FacialTimelineValidationReport report = FacialTimelineValidator.Validate(timeline, CreateProfile());
                ClipDrawOptions options = FacialValueClipEditor.BuildClipOptions(clip, new ClipDrawOptions(), report);

                Assert.That(options.errorText, Does.Contain("[-1, 1]"));
                Assert.That(options.tooltip, Does.Contain("[-1, 1]"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(timeline);
            }
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
                    new Expression(
                        id: "smile",
                        name: "Smile",
                        layer: "Expressions",
                        transitionDuration: 0.25f,
                        transitionCurve: TransitionCurve.Linear,
                        blendShapeValues: new[]
                        {
                            new BlendShapeMapping("Smile", 1f),
                        }),
                });
        }
    }
}
