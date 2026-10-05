using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class RecToTimelineExporterTests : SizedTestFixture
    {
        [Test]
        public void RecEventSequenceAdapter_NewRecKinds_AreSkippedAndExistingEventsArePreserved()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateValueProviderSample(0.20d, 1, RecValueProviderFlags.None, 0, 0),
                    RecEvent.CreateExpressionActivate(0.30d, 0, 1),
                    RecEvent.CreateAnalogSample(0.40d, 1, 2),
                    RecEvent.CreateExpressionDeactivate(0.50d, 0, 1),
                    RecEvent.CreateTriggerOff(0.60d, 0, 0),
                },
                new[] { "input:trigger", "value:provider" },
                new[] { "smile", "blink" },
                1.0d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                    new[] { 0.25f, -0.25f },
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                });

            var sequence = new RecEventSequenceAdapter(timeline);

            Assert.That(sequence.Count, Is.EqualTo(3));
            Assert.That(sequence[0].Kind, Is.EqualTo(RecordedEventKind.TriggerOn));
            Assert.That(sequence[1].Kind, Is.EqualTo(RecordedEventKind.AnalogValue));
            Assert.That(sequence[1].SourceId, Is.EqualTo("value:provider"));
            Assert.That(sequence[2].Kind, Is.EqualTo(RecordedEventKind.TriggerOff));
            Assert.That(sequence[2].ExpressionId, Is.EqualTo("smile"));
        }

        [Test]
        public void CreateTimelineAsset_BuildsExpressionLanes_ClosesDanglingOnAtDuration_AndWarnsForMissingExpressions()
        {
            var sequence = new FakeRecordedEventSequence(
                2.0d,
                new[]
                {
                    new RecordedEvent(0.10d, RecordedEventKind.TriggerOn, expressionId: "smile"),
                    new RecordedEvent(0.20d, RecordedEventKind.TriggerOn, expressionId: "angry"),
                    new RecordedEvent(0.40d, RecordedEventKind.TriggerOff, expressionId: "angry"),
                    new RecordedEvent(0.60d, RecordedEventKind.TriggerOn, expressionId: "missing"),
                    new RecordedEvent(0.70d, RecordedEventKind.TriggerOff, expressionId: "missing"),
                    new RecordedEvent(0.80d, RecordedEventKind.TriggerOff, expressionId: "smile"),
                    new RecordedEvent(1.20d, RecordedEventKind.TriggerOn, expressionId: "smile"),
                });

            TimelineAsset timeline = null;
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[RecToTimelineExporter\].*'missing'"));

                timeline = Editor.RecToTimelineExporter.CreateTimelineAsset(sequence, CreateProfile());

                var rootTrack = FindTrack<FacialExpressionTrack>(timeline, "emotion");
                Assert.That(rootTrack, Is.Not.Null);

                TimelineClip[] rootClips = ToArray(rootTrack.GetClips());
                Assert.That(rootClips, Has.Length.EqualTo(2));
                AssertClip(rootClips[0], "smile", 0.10d, 0.80d);
                AssertClip(rootClips[1], "smile", 1.20d, 2.0d);

                TrackAsset[] childTracks = ToArray(rootTrack.GetChildTracks());
                Assert.That(childTracks, Has.Length.EqualTo(1));

                TimelineClip[] childClips = ToArray(childTracks[0].GetClips());
                Assert.That(childClips, Has.Length.EqualTo(2));
                AssertClip(childClips[0], "angry", 0.20d, 0.40d);
                AssertClip(childClips[1], "missing", 0.60d, 0.70d);
            }
            finally
            {
                if (timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(timeline);
                }
            }
        }

        [Test]
        public void CreateTimelineAsset_BuildsAnalogAndGazeTracks_PreservesMultiAxis_AndFallsBackMismatchedGazeToAnalog()
        {
            var sequence = new FakeRecordedEventSequence(
                1.0d,
                new[]
                {
                    new RecordedEvent(0.10d, RecordedEventKind.AnalogValue, sourceId: "analog:mouth", axes: new[] { 0.25f, -0.5f, 0.75f }),
                    new RecordedEvent(0.20d, RecordedEventKind.AnalogValue, sourceId: "live:gaze", axes: new[] { -1f, 1f }),
                    new RecordedEvent(0.30d, RecordedEventKind.AnalogValue, sourceId: "live:gaze-bad", axes: new[] { 0.1f, 0.2f, 0.3f }),
                    new RecordedEvent(0.50d, RecordedEventKind.AnalogValue, sourceId: "analog:mouth", axes: new[] { 0.5f, -0.25f, 0.0f }),
                    new RecordedEvent(0.60d, RecordedEventKind.AnalogValue, sourceId: "live:gaze", axes: new[] { 0.5f, -0.5f }),
                });

            TimelineAsset timeline = null;
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[RecToTimelineExporter\].*'live:gaze-bad'"));

                timeline = Editor.RecToTimelineExporter.CreateTimelineAsset(
                    sequence,
                    CreateProfile(),
                    new HashSet<string>(StringComparer.Ordinal)
                    {
                        "live:gaze",
                        "live:gaze-bad",
                    });

                var analogTrack = FindTrack<FacialValueTrack>(timeline, "analog:mouth");
                Assert.That(analogTrack, Is.Not.Null);
                Assert.That(analogTrack.ChannelKind, Is.EqualTo(FacialValueChannelKind.Analog));
                Assert.That(analogTrack.ChannelSubId, Is.EqualTo("analog:mouth"));

                var analogClip = (FacialValueClip)ToArray(analogTrack.GetClips())[0].asset;
                Assert.That(analogClip.Axes, Has.Length.EqualTo(3));
                Assert.That(analogClip.Axes[0].keys[0].time, Is.EqualTo(0f).Within(1e-6f));
                Assert.That(analogClip.Axes[2].keys[1].value, Is.EqualTo(0f).Within(1e-6f));

                var gazeTrack = FindTrack<FacialValueTrack>(timeline, "live:gaze");
                Assert.That(gazeTrack, Is.Not.Null);
                Assert.That(gazeTrack.ChannelKind, Is.EqualTo(FacialValueChannelKind.Gaze));
                var gazeClip = (FacialValueClip)ToArray(gazeTrack.GetClips())[0].asset;
                Assert.That(gazeClip.Axes, Has.Length.EqualTo(2));
                Assert.That(gazeClip.Axes[0].keys[1].value, Is.EqualTo(0.5f).Within(1e-6f));

                var badGazeTrack = FindTrack<FacialValueTrack>(timeline, "live:gaze-bad");
                Assert.That(badGazeTrack, Is.Not.Null);
                Assert.That(badGazeTrack.ChannelKind, Is.EqualTo(FacialValueChannelKind.Analog));
                var badGazeClip = (FacialValueClip)ToArray(badGazeTrack.GetClips())[0].asset;
                Assert.That(badGazeClip.Axes, Has.Length.EqualTo(3));
            }
            finally
            {
                if (timeline != null)
                {
                    UnityEngine.Object.DestroyImmediate(timeline);
                }
            }
        }

        // ================================================================
        // チャネル検出（種別と理由）
        // ================================================================

        [Test]
        public void DetectChannels_ExplicitGazeSourceId_IsGazeWithExplicitReason()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                profile.WritableGazeChannels[0].sourceIdLeft = "custom:eyeL";

                ChannelDetection detection = DetectSingle(profile, "custom:eyeL", 2);

                Assert.That(detection.Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
                Assert.That(detection.Reason, Is.EqualTo(ChannelDetectionReason.ExplicitGazeSourceId));
                Assert.That(detection.AxisCount, Is.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DetectChannels_ConventionIdOfProfileGazeChannel_IsGazeWithConventionReason()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                ChannelDetection detection = DetectSingle(profile, "osc:gaze", 2);

                Assert.That(detection.Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
                Assert.That(detection.Reason, Is.EqualTo(ChannelDetectionReason.ConventionGazeChannel));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DetectChannels_BindingGazeDeclaration_IsGazeWithProviderReason()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                profile.WritableAdapterBindings.Add(new GazeDeclaringBinding("vmc", "look"));
                profile.WritableAdapterBindings.Add(new GazeDeclaringBinding("ifm", null));

                ChannelDetection declared = DetectSingle(profile, "vmc:look", 2);
                ChannelDetection wildcard = DetectSingle(profile, "ifm:eyes", 2);
                ChannelDetection otherChannel = DetectSingle(profile, "vmc:other", 2);

                Assert.That(declared.Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
                Assert.That(declared.Reason, Is.EqualTo(ChannelDetectionReason.GazeProviderDeclaration));
                Assert.That(wildcard.Kind, Is.EqualTo(FacialValueChannelKind.Gaze), "ワイルドカード宣言は slug 一致で Gaze");
                Assert.That(wildcard.Reason, Is.EqualTo(ChannelDetectionReason.GazeProviderDeclaration));
                Assert.That(otherChannel.Kind, Is.EqualTo(FacialValueChannelKind.Analog), "宣言に無いチャネルは Gaze にしない");
                Assert.That(otherChannel.Reason, Is.EqualTo(ChannelDetectionReason.DefaultAnalog));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DetectChannels_GazeCandidateWithNonTwoAxisSamples_IsAnalogWithNonTwoAxisReason()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[RecToTimelineExporter\].*'osc:gaze'"));

                ChannelDetection detection = DetectSingle(profile, "osc:gaze", 3);

                Assert.That(detection.Kind, Is.EqualTo(FacialValueChannelKind.Analog));
                Assert.That(detection.Reason, Is.EqualTo(ChannelDetectionReason.NonTwoAxisSamples));
                Assert.That(detection.AxisCount, Is.EqualTo(3));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DetectChannels_UnrelatedSource_IsDefaultAnalog()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                ChannelDetection detection = DetectSingle(profile, "osc:lt", 1);

                Assert.That(detection.SourceId, Is.EqualTo("osc:lt"));
                Assert.That(detection.Kind, Is.EqualTo(FacialValueChannelKind.Analog));
                Assert.That(detection.Reason, Is.EqualTo(ChannelDetectionReason.DefaultAnalog));
                Assert.That(detection.AxisCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DetectChannels_KindOverride_UsesOverrideWithOverriddenReason()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                var sequence = new FakeRecordedEventSequence(
                    1.0d,
                    new[] { new RecordedEvent(0.1d, RecordedEventKind.AnalogValue, sourceId: "osc:lt", axes: new[] { 0.1f, 0.2f }) });
                var overrides = new Dictionary<string, FacialValueChannelKind>(StringComparer.Ordinal)
                {
                    ["osc:lt"] = FacialValueChannelKind.Gaze,
                };

                IReadOnlyList<ChannelDetection> detections = Editor.RecToTimelineExporter.DetectChannels(sequence, profile, overrides);

                Assert.That(detections, Has.Count.EqualTo(1));
                Assert.That(detections[0].Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
                Assert.That(detections[0].Reason, Is.EqualTo(ChannelDetectionReason.Overridden));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        [Test]
        public void DetectChannels_TriggerOnlySource_IsNotListedAndOrderIsBySourceId()
        {
            DetectionProfileSO profile = CreateDetectionProfile();
            try
            {
                var sequence = new FakeRecordedEventSequence(
                    1.0d,
                    new[]
                    {
                        new RecordedEvent(0.1d, RecordedEventKind.TriggerOn, expressionId: "smile", sourceId: "input:trigger"),
                        new RecordedEvent(0.2d, RecordedEventKind.AnalogValue, sourceId: "osc:lt", axes: new[] { 0.5f }),
                        new RecordedEvent(0.3d, RecordedEventKind.AnalogValue, sourceId: "osc:gaze", axes: new[] { 0.1f, 0.2f }),
                        new RecordedEvent(0.4d, RecordedEventKind.TriggerOff, expressionId: "smile", sourceId: "input:trigger"),
                    });

                IReadOnlyList<ChannelDetection> detections = Editor.RecToTimelineExporter.DetectChannels(sequence, profile);

                Assert.That(detections, Has.Count.EqualTo(2), "Analog イベントを持たないトリガー専用 source は含めない");
                Assert.That(detections[0].SourceId, Is.EqualTo("osc:gaze"));
                Assert.That(detections[1].SourceId, Is.EqualTo("osc:lt"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(profile);
            }
        }

        private static ChannelDetection DetectSingle(FacialCharacterProfileSO profile, string sourceId, int axisCount)
        {
            var sequence = new FakeRecordedEventSequence(
                1.0d,
                new[] { new RecordedEvent(0.1d, RecordedEventKind.AnalogValue, sourceId: sourceId, axes: new float[axisCount]) });
            IReadOnlyList<ChannelDetection> detections = Editor.RecToTimelineExporter.DetectChannels(sequence, profile);
            Assert.That(detections, Has.Count.EqualTo(1));
            return detections[0];
        }

        private static DetectionProfileSO CreateDetectionProfile()
        {
            var profile = ScriptableObject.CreateInstance<DetectionProfileSO>();
            Assert.That(profile.GazeChannels[0].id, Is.EqualTo("gaze"), "前提: 既定の Gaze チャネル");
            return profile;
        }

        private sealed class DetectionProfileSO : FacialCharacterProfileSO
        {
            public List<GazeChannel> WritableGazeChannels
            {
                get
                {
                    _ = GazeChannels;
                    return _gazeChannels;
                }
            }

            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
        }

        private sealed class GazeDeclaringBinding : AdapterBindingBase, IGazeSourceProvider
        {
            private readonly string _channelId;

            public GazeDeclaringBinding(string slug, string channelId)
            {
                Slug = slug;
                _channelId = channelId;
            }

            public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
            {
                yield return new GazeSourceDeclaration(_channelId, providesLeftRightPair: false);
            }
        }

        private static FacialProfile CreateProfile()
        {
            return new FacialProfile(
                "1.0.0",
                layers: new[]
                {
                    new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                },
                expressions: new[]
                {
                    new Expression("smile", "Smile", "emotion"),
                    new Expression("angry", "Angry", "emotion"),
                });
        }

        private static T FindTrack<T>(TimelineAsset timeline, string name) where T : TrackAsset
        {
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                if (track is T typed && string.Equals(track.name, name, StringComparison.Ordinal))
                {
                    return typed;
                }
            }

            return null;
        }

        private static void AssertClip(TimelineClip clip, string expressionId, double start, double end)
        {
            Assert.That(clip.start, Is.EqualTo(start).Within(1e-9d));
            Assert.That(clip.end, Is.EqualTo(end).Within(1e-9d));
            Assert.That(((FacialExpressionClip)clip.asset).ExpressionId, Is.EqualTo(expressionId));
        }

        private static T[] ToArray<T>(IEnumerable<T> items)
        {
            var list = new List<T>();
            foreach (T item in items)
            {
                list.Add(item);
            }

            return list.ToArray();
        }

        private sealed class FakeRecordedEventSequence : IRecordedEventSequence
        {
            private readonly RecordedEvent[] _events;

            public FakeRecordedEventSequence(double durationSeconds, RecordedEvent[] events)
            {
                DurationSeconds = durationSeconds;
                _events = events ?? Array.Empty<RecordedEvent>();
            }

            public double DurationSeconds { get; }

            public int Count => _events.Length;

            public RecordedEvent this[int index] => _events[index];
        }
    }
}
