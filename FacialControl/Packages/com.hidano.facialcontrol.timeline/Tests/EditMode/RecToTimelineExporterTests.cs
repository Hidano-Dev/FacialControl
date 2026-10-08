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
        public void RecEventSequenceAdapter_ValueProviderConvertedAndExpressionKindsCounted_ExistingEventsPreserved()
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

            Assert.That(sequence.Count, Is.EqualTo(4));
            Assert.That(sequence[0].Kind, Is.EqualTo(RecordedEventKind.TriggerOn));
            Assert.That(sequence[1].Kind, Is.EqualTo(RecordedEventKind.ValueProviderSample), "kind 7 は値提供型の記録として残す");
            Assert.That(sequence[1].SourceId, Is.EqualTo("value:provider"));
            Assert.That(sequence[1].IsValid, Is.False);
            Assert.That(sequence[2].Kind, Is.EqualTo(RecordedEventKind.AnalogValue));
            Assert.That(sequence[2].SourceId, Is.EqualTo("value:provider"));
            Assert.That(sequence[3].Kind, Is.EqualTo(RecordedEventKind.TriggerOff));
            Assert.That(sequence[3].ExpressionId, Is.EqualTo("smile"));
            Assert.That(sequence.SkippedExpressionEventCount, Is.EqualTo(2), "kind 9 / 10 は読み捨てて件数を数える");
            Assert.That(sequence.SkippedWeightEventCount, Is.EqualTo(0));
        }

        [Test]
        public void RecEventSequenceAdapter_ValueProviderBaselineAndSamples_ConvertsWithMaskValuesAndValidity()
        {
            var baseline = new RecBaselineState(
                null,
                null,
                new[] { new RecBaselineState.ValueProviderEntry("ifm", true, new byte[] { 0b0000_0011 }, new[] { 0.1f, 0.2f }) },
                null);
            var timeline = new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateValueProviderSample(
                        0.5d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasValues, 2, 0),
                    RecEvent.CreateValueProviderSample(
                        0.6d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues, 1, 1),
                    RecEvent.CreateValueProviderSample(0.7d, 0, RecValueProviderFlags.None, 0, 0),
                },
                new[] { "ifm" },
                Array.Empty<string>(),
                1.0d,
                new IReadOnlyList<float>[] { new[] { 0.3f, 0.4f }, new[] { 0.9f }, Array.Empty<float>() },
                new IReadOnlyList<byte>[] { Array.Empty<byte>(), new byte[] { 0b0000_0100 }, Array.Empty<byte>() });

            var sequence = new RecEventSequenceAdapter(timeline);

            Assert.That(sequence.Count, Is.EqualTo(4), "基準 1 件 + 時刻付き 3 件");
            Assert.That(sequence[0].TimeSeconds, Is.EqualTo(0d), "基準（kind 8）は t=0 の状態");
            Assert.That(sequence[0].IsValid, Is.True);
            Assert.That(sequence[0].MaskBytes, Is.EqualTo(new byte[] { 0b0000_0011 }));
            Assert.That(sequence[0].Axes, Is.EqualTo(new[] { 0.1f, 0.2f }));
            Assert.That(sequence[1].TimeSeconds, Is.EqualTo(0.5d));
            Assert.That(sequence[1].MaskBytes, Is.Empty, "mask を載せないレコードは空（従来の mask を使う）");
            Assert.That(sequence[1].Axes, Is.EqualTo(new[] { 0.3f, 0.4f }));
            Assert.That(sequence[2].MaskBytes, Is.EqualTo(new byte[] { 0b0000_0100 }));
            Assert.That(sequence[2].Axes, Is.EqualTo(new[] { 0.9f }));
            Assert.That(sequence[3].IsValid, Is.False);
            Assert.That(sequence[3].Axes, Is.Empty);
        }

        // ================================================================
        // 値提供型トラック
        // ================================================================

        [Test]
        public void CreateTimelineAsset_ValueProvider_BuildsStepCurvesFromBaselineWithMaskAndValidity()
        {
            var sequence = new FakeRecordedEventSequence(
                2.0d,
                new[]
                {
                    // 基準: index 0 / 1 が寄与、有効。
                    RecordedEvent.CreateValueProviderSample(0d, "ifm", true, new byte[] { 0b0000_0011 }, new[] { 0.1f, 0.2f }),
                    // 値だけ更新。
                    RecordedEvent.CreateValueProviderSample(0.5d, "ifm", true, null, new[] { 0.3f, 0.4f }),
                    // mask を index 2 だけに変更（index 0 / 1 の値は 0 にクリア）。
                    RecordedEvent.CreateValueProviderSample(1.0d, "ifm", true, new byte[] { 0b0000_0100 }, new[] { 0.9f }),
                    // 無効化（mask / 値は保持）。
                    RecordedEvent.CreateValueProviderSample(1.5d, "ifm", false, null, null),
                });

            TimelineAsset timeline = null;
            try
            {
                timeline = Editor.RecToTimelineExporter.CreateTimelineAsset(sequence, CreateProfile());

                var track = FindTrack<FacialValueTrack>(timeline, "ifm");
                Assert.That(track, Is.Not.Null);
                Assert.That(track.ChannelKind, Is.EqualTo(FacialValueChannelKind.ValueProvider));
                Assert.That(track.ChannelSubId, Is.EqualTo("ifm"));

                TimelineClip clip = ToArray(track.GetClips())[0];
                Assert.That(clip.start, Is.EqualTo(0d), "基準があれば t=0 から");
                Assert.That(clip.end, Is.EqualTo(2.0d).Within(1e-9d));

                var valueClip = (FacialValueClip)clip.asset;
                Assert.That(valueClip.BlendShapeIndices, Is.EqualTo(new[] { 0, 1, 2 }), "寄与したことのある index だけを軸にする");
                Assert.That(valueClip.BlendShapeNames, Is.EqualTo(new[] { string.Empty, string.Empty, string.Empty }), "名前列なしは index 保存");

                AssertCurve(valueClip.Axes[0], (0.25f, 0.1f), (0.75f, 0.3f), (1.25f, 0f));
                AssertCurve(valueClip.Axes[2], (0.25f, 0f), (1.0f, 0.9f), (1.75f, 0.9f));
                AssertCurve(valueClip.Contributes[0], (0.99f, 1f), (1.0f, 0f));
                AssertCurve(valueClip.Contributes[2], (0.99f, 0f), (1.0f, 1f));
                AssertCurve(valueClip.Validity, (1.49f, 1f), (1.5f, 0f));
                Assert.That(float.IsPositiveInfinity(valueClip.Axes[0].keys[0].outTangent), Is.True, "REC 再生と同じく次のサンプルまで値を保持する（階段）");
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
        public void CreateTimelineAsset_ValueProviderWithoutBaseline_StartsAtFirstRecordAndSkipsSourcesWithoutMask()
        {
            var sequence = new FakeRecordedEventSequence(
                1.0d,
                new[]
                {
                    RecordedEvent.CreateValueProviderSample(0.2d, "udp", false, null, null),
                    RecordedEvent.CreateValueProviderSample(0.3d, "lipsync", true, new byte[] { 0b0000_0001 }, new[] { 0.5f }),
                });

            TimelineAsset timeline = null;
            try
            {
                timeline = Editor.RecToTimelineExporter.CreateTimelineAsset(sequence, CreateProfile());

                Assert.That(FindTrack<FacialValueTrack>(timeline, "udp"), Is.Null, "寄与 BlendShape の無い source はトラックにしない");
                var track = FindTrack<FacialValueTrack>(timeline, "lipsync");
                Assert.That(track, Is.Not.Null);
                Assert.That(ToArray(track.GetClips())[0].start, Is.EqualTo(0.3d).Within(1e-9d), "基準が無ければ最初のレコードから");
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
        public void CreateTimelineAsset_ValueProviderWithMatchingReferenceNames_StoresNames()
        {
            var sequence = ValueProviderSequence(new byte[] { 0b0000_0101 }, new[] { 0.4f, 0.6f });
            string[] referenceNames = { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen" };

            TimelineAsset timeline = null;
            try
            {
                timeline = Editor.RecToTimelineExporter.CreateTimelineAsset(
                    sequence, CreateProfile(), referenceBlendShapeNames: referenceNames);

                var clip = (FacialValueClip)ToArray(FindTrack<FacialValueTrack>(timeline, "ifm").GetClips())[0].asset;
                Assert.That(clip.BlendShapeIndices, Is.EqualTo(new[] { 0, 2 }));
                Assert.That(clip.BlendShapeNames, Is.EqualTo(new[] { "eyeBlinkLeft", "jawOpen" }));
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
        public void CreateTimelineAsset_ValueProviderWithMismatchedReferenceNames_FallsBackToIndexAndWarns()
        {
            // mask は 2 バイト（BlendShape 9〜16 個）なのに参照名は 3 個 → 別モデルとみなして index 保存。
            var sequence = ValueProviderSequence(new byte[] { 0b0000_0001, 0b0000_0001 }, new[] { 0.4f, 0.6f });
            string[] referenceNames = { "eyeBlinkLeft", "eyeBlinkRight", "jawOpen" };

            TimelineAsset timeline = null;
            try
            {
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[RecToTimelineExporter\].*BlendShape names.*'ifm'"));

                timeline = Editor.RecToTimelineExporter.CreateTimelineAsset(
                    sequence, CreateProfile(), referenceBlendShapeNames: referenceNames);

                var clip = (FacialValueClip)ToArray(FindTrack<FacialValueTrack>(timeline, "ifm").GetClips())[0].asset;
                Assert.That(clip.BlendShapeIndices, Is.EqualTo(new[] { 0, 8 }));
                Assert.That(clip.BlendShapeNames, Is.EqualTo(new[] { string.Empty, string.Empty }));
                LogAssert.NoUnexpectedReceived();
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
        public void DetectChannels_ValueProviderSource_IsListedWithBlendShapeCountAndReason()
        {
            var sequence = new FakeRecordedEventSequence(
                1.0d,
                new[]
                {
                    new RecordedEvent(0.1d, RecordedEventKind.AnalogValue, sourceId: "ifm:head", axes: new[] { 0.1f, 0.2f, 0.3f }),
                    RecordedEvent.CreateValueProviderSample(0.1d, "ifm", true, new byte[] { 0b0000_0111 }, new[] { 0.1f, 0.2f, 0.3f }),
                    RecordedEvent.CreateValueProviderSample(0.2d, "udp", true, new byte[] { 0b0000_0001 }, new[] { 0.5f }),
                });

            IReadOnlyList<ChannelDetection> detections = Editor.RecToTimelineExporter.DetectChannels(sequence, profileAsset: null);

            Assert.That(detections, Has.Count.EqualTo(3));
            Assert.That(detections[0].SourceId, Is.EqualTo("ifm"), "source id 昇順");
            Assert.That(detections[0].Kind, Is.EqualTo(FacialValueChannelKind.ValueProvider));
            Assert.That(detections[0].AxisCount, Is.EqualTo(3), "寄与した BlendShape の数");
            Assert.That(detections[0].Reason, Is.EqualTo(ChannelDetectionReason.ValueProviderIndexed));
            Assert.That(detections[1].SourceId, Is.EqualTo("ifm:head"));
            Assert.That(detections[1].Kind, Is.EqualTo(FacialValueChannelKind.Analog));
            Assert.That(detections[2].SourceId, Is.EqualTo("udp"));
            Assert.That(RecTimelineExportWindow.KindLabel(detections[0]), Is.EqualTo("ValueProvider（3 個の BlendShape）"));
            Assert.That(RecTimelineExportWindow.KindLabel(detections[1]), Is.EqualTo("Analog（3 軸）"));
            Assert.That(RecTimelineExportWindow.ReasonLabel(detections[0].Reason), Does.Contain("値提供型"));
            Assert.That(RecTimelineExportWindow.ReasonLabel(ChannelDetectionReason.ValueProviderNamed), Does.Contain("名前"));
        }

        private static FakeRecordedEventSequence ValueProviderSequence(byte[] mask, float[] values)
        {
            return new FakeRecordedEventSequence(
                1.0d,
                new[] { RecordedEvent.CreateValueProviderSample(0d, "ifm", true, mask, values) });
        }

        private static void AssertCurve(AnimationCurve curve, params (float time, float value)[] samples)
        {
            Assert.That(curve, Is.Not.Null);
            for (int i = 0; i < samples.Length; i++)
            {
                Assert.That(curve.Evaluate(samples[i].time), Is.EqualTo(samples[i].value).Within(1e-6f), $"t={samples[i].time}");
            }
        }

        [Test]
        public void RecEventSequenceAdapter_WeightKinds_AreSkippedAndCountedWithoutThrowing()
        {
            // rec-weight-coverage Req 7.7: weight の時刻付き kind（12 / 13）を含む REC でも例外にせず、
            // Export 対象のレコードだけを残す。読み捨てた件数は Export 時の警告用に保持する。
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("emotion", 1f) },
                new[] { new InputSourceWeightEntry("emotion", "input:trigger", 1f) });
            var timeline = new RecTimeline(
                baseline,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateLayerWeightSample(0.20d, 0),
                    RecEvent.CreateAnalogSample(0.30d, 1, 2),
                    RecEvent.CreateInputSourceWeightSample(0.40d, 0, 0),
                    RecEvent.CreateTriggerOff(0.60d, 0, 0),
                },
                new[] { "input:trigger", "analog:mouth" },
                new[] { "smile" },
                new[] { "emotion" },
                1.0d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new[] { 0.5f },
                    new[] { 0.25f, -0.25f },
                    new[] { 0.75f },
                    Array.Empty<float>(),
                });

            var sequence = new RecEventSequenceAdapter(timeline);

            Assert.That(sequence.Count, Is.EqualTo(3));
            Assert.That(sequence[0].Kind, Is.EqualTo(RecordedEventKind.TriggerOn));
            Assert.That(sequence[1].Kind, Is.EqualTo(RecordedEventKind.AnalogValue));
            Assert.That(sequence[1].SourceId, Is.EqualTo("analog:mouth"));
            Assert.That(sequence[2].Kind, Is.EqualTo(RecordedEventKind.TriggerOff));
            Assert.That(sequence.SkippedWeightEventCount, Is.EqualTo(2));
        }

        [Test]
        public void RecEventSequenceAdapter_NoWeightKinds_ReportsZeroSkippedWeightEvents()
        {
            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateTriggerOff(0.60d, 0, 0),
                },
                new[] { "input:trigger" },
                new[] { "smile" },
                1.0d);

            var sequence = new RecEventSequenceAdapter(timeline);

            Assert.That(sequence.SkippedWeightEventCount, Is.EqualTo(0));
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
