using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [MediumTest]
    public sealed class RecToTimelineExportWorkflowTests : SizedTestFixture
    {
        [Test]
        public void TryExportTimelineAsset_NewAsset_CreatesTimelineAndBakeWithoutTouchingSceneObjects()
        {
            ExportFixture fixture = ExportFixture.Create();
            var host = new GameObject("RecTimelineExportHost");
            var director = host.AddComponent<PlayableDirector>();
            var receiver = host.AddComponent<FacialTimelineReceiver>();

            try
            {
                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.True);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Timeline, Is.Not.Null);
                Assert.That(result.BakeAsset, Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<TimelineAsset>(fixture.TimelinePath), Is.Not.Null);

                // Export は TimelineAsset 1 つで完結し、シーン上の Director / Receiver に副作用を持たない（Req 10.5）。
                Assert.That(director.playableAsset, Is.Null);
                Assert.That(receiver.BakeAsset, Is.Null);
                foreach (TrackAsset track in result.Timeline.GetOutputTracks())
                {
                    Assert.That(director.GetGenericBinding(track), Is.Null);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_ReturnsDetectionsAndChannelSubIdsAreValidForPlayback()
        {
            ExportFixture fixture = ExportFixture.Create();

            try
            {
                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.True);
                Assert.That(result.ChannelDetections, Has.Count.EqualTo(1), "Analog イベントを持つ source だけが検出される");
                Assert.That(result.ChannelDetections[0].SourceId, Is.EqualTo("live:gaze"));
                Assert.That(result.ChannelDetections[0].Kind, Is.EqualTo(FacialValueChannelKind.Gaze));
                Assert.That(result.ChannelDetections[0].Reason, Is.EqualTo(ChannelDetectionReason.ConventionGazeChannel));

                FacialProfile profile = TimelineProfileSource.Resolve(fixture.Profile);
                var derivation = Hidano.FacialControl.Timeline.Domain.Services.TimelineChannelDeriver.Derive(
                    TimelineAssetScanner.Scan(result.Timeline).Tracks, profile);
                Assert.That(derivation.InvalidChannelSubIds, Is.Empty, "Export した ChannelSubId を再生側が不正扱いしない");
                Assert.That(derivation.Channels, Has.Count.EqualTo(1));
                Assert.That(derivation.Channels[0].ChannelSubId, Is.EqualTo("live:gaze"));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_ExistingAsset_CancelledOverwriteLeavesTimelineUntouched()
        {
            ExportFixture fixture = ExportFixture.Create();
            TimelineAsset existingTimeline = CreateExistingTimeline(fixture.TimelinePath);
            Func<string, string, string, string, bool> originalDialog = RecToTimelineExporter.ConfirmOverwriteDialog;

            try
            {
                bool dialogShown = false;
                RecToTimelineExporter.ConfirmOverwriteDialog = (title, message, ok, cancel) =>
                {
                    dialogShown = true;
                    return false;
                };

                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.False);
                Assert.That(result.Cancelled, Is.True);
                Assert.That(dialogShown, Is.True);
                Assert.That(ToArray(existingTimeline.GetOutputTracks()), Has.Length.EqualTo(1));
                Assert.That(existingTimeline.GetOutputTracks().GetEnumerator().MoveNext(), Is.True);
            }
            finally
            {
                RecToTimelineExporter.ConfirmOverwriteDialog = originalDialog;
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_WhenRecLoadFails_DoesNotModifyExistingTimeline()
        {
            ExportFixture fixture = ExportFixture.Create();
            TimelineAsset existingTimeline = CreateExistingTimeline(fixture.TimelinePath);
            string missingRecPath = Path.Combine(Path.GetDirectoryName(fixture.RecordingAbsolutePath) ?? string.Empty, "missing.rec");

            try
            {
                LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("REC load failed"));

                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    missingRecPath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.False);
                Assert.That(result.Success, Is.False);
                Assert.That(result.Cancelled, Is.False);
                Assert.That(ToArray(existingTimeline.GetOutputTracks()), Has.Length.EqualTo(1));
                Assert.That(FindBakeAsset(fixture.TimelinePath), Is.Null);
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_WithChildLanes_WritesSameBakeToAllFacialTracksAndLocatorFindsIt()
        {
            ExportFixture fixture = ExportFixture.Create(overlappingTriggers: true);

            try
            {
                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.True);
                TimelineAsset exported = AssetDatabase.LoadAssetAtPath<TimelineAsset>(fixture.TimelinePath);
                IReadOnlyList<TrackAsset> facialTracks = TimelineAssetScanner.Scan(exported).TrackAssets;

                int childCount = 0;
                for (int i = 0; i < facialTracks.Count; i++)
                {
                    if (facialTracks[i].parent is TrackAsset)
                    {
                        childCount++;
                    }

                    Assert.That(
                        ((IFacialTimelineBakeHolder)facialTracks[i]).Bake,
                        Is.SameAs(result.BakeAsset),
                        $"track '{facialTracks[i].name}'");
                }

                Assert.That(childCount, Is.GreaterThan(0), "前提: 重なる Trigger から子レーンが生成される");
                Assert.That(facialTracks.Count, Is.GreaterThanOrEqualTo(3), "前提: root / 子 / Value トラックを含む");

                BakeLocateResult located = FacialTimelineBakeLocator.Locate(exported, null);
                Assert.That(located.Status, Is.EqualTo(BakeLocateStatus.Found));
                Assert.That(located.Bake, Is.SameAs(result.BakeAsset));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_BakeSubAsset_IsHiddenInHierarchy()
        {
            ExportFixture fixture = ExportFixture.Create();

            try
            {
                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out _);

                Assert.That(success, Is.True);
                FacialTimelineBakeAsset bake = FindBakeAsset(fixture.TimelinePath);
                Assert.That(bake, Is.Not.Null);
                Assert.That(bake.hideFlags & HideFlags.HideInHierarchy, Is.EqualTo(HideFlags.HideInHierarchy));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_ProfileJsonDiffersFromSo_UsesJsonProfile()
        {
            using (ProfileJsonTestFixture json = ProfileJsonTestFixture.Create("RecToTimelineExportWorkflowJsonTests", "smile"))
            {
                // SO に無い overlay レイヤーと wink を JSON 側にだけ置く。
                json.WriteProfileJsonWithExtraLayer(
                    new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    "overlay",
                    "wink",
                    "smile");
                string recordingPath = json.FolderPath + "/wink.rec";
                File.WriteAllBytes(recordingPath, RecBinaryFormat.Serialize(CreateSingleTriggerRecording("wink"), 123L));
                AssetDatabase.Refresh();

                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    Path.GetFullPath(recordingPath),
                    json.ProfileAsset,
                    json.FolderPath + "/Exported.playable",
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.True);
                var rootExpressionTrackNames = new List<string>();
                foreach (TrackAsset track in result.Timeline.GetRootTracks())
                {
                    if (track is FacialExpressionTrack)
                    {
                        rootExpressionTrackNames.Add(track.name);
                    }
                }

                Assert.That(rootExpressionTrackNames, Is.EquivalentTo(new[] { "overlay" }), "wink は JSON 側のレイヤーへ出力される");
                Assert.That(
                    result.BakeAsset.ProfileContentHashHex,
                    Is.EqualTo(json.ContentHash(json.ProfileAsset.LoadProfile())));
            }
        }

        [Test]
        public void TryExportTimelineAsset_RecWithWeightRecords_ExportsConvertibleRecordsAndWarnsOnce()
        {
            // rec-weight-coverage Req 7.7: weight の基準エントリ（kind 14 / 15）と時刻付き weight（kind 12 / 13）を含む
            // 現行形式の .fcrec を、例外なく読んで Export できる。weight は Timeline の表現を持たないため
            // Export 対象外として読み捨て、その旨を Export 1 回につき 1 回だけ警告する（無言で捨てない）。
            ExportFixture fixture = ExportFixture.Create(includeWeightRecords: true);

            try
            {
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"\[RecToTimelineExporter\].*2 weight record"));

                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.True);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Timeline, Is.Not.Null);

                var expressionTracks = new List<FacialExpressionTrack>();
                var valueTracks = new List<FacialValueTrack>();
                foreach (TrackAsset track in result.Timeline.GetRootTracks())
                {
                    if (track is FacialExpressionTrack expressionTrack)
                    {
                        expressionTracks.Add(expressionTrack);
                    }
                    else if (track is FacialValueTrack valueTrack)
                    {
                        valueTracks.Add(valueTrack);
                    }
                }

                Assert.That(expressionTracks, Has.Count.EqualTo(1));
                TimelineClip[] clips = ToArray(expressionTracks[0].GetClips());
                Assert.That(clips, Has.Length.EqualTo(1));
                Assert.That(((FacialExpressionClip)clips[0].asset).ExpressionId, Is.EqualTo("smile"));
                Assert.That(clips[0].start, Is.EqualTo(0.10d).Within(1e-6));
                Assert.That(clips[0].end, Is.EqualTo(0.60d).Within(1e-6));

                Assert.That(valueTracks, Has.Count.EqualTo(1), "weight は値トラックにならない");
                Assert.That(valueTracks[0].ChannelSubId, Is.EqualTo("live:gaze"));

                // 警告は 1 回だけ（LogAssert.Expect で 1 件消費済み。2 件目があれば未期待ログとして失敗する）。
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_RecWithExpressionActivateRecords_WarnsOnceWithCount()
        {
            // 既定 fixture は kind 9 / 10（系1 の activate / deactivate）を 1 件ずつ含む。Timeline に表現が無いため読み捨てるが、
            // weight と同じく Export 1 回につき 1 回、件数付きで警告する（無言で捨てない）。
            ExportFixture fixture = ExportFixture.Create();

            try
            {
                LogAssert.Expect(
                    LogType.Warning,
                    new System.Text.RegularExpressions.Regex(@"\[RecToTimelineExporter\].*2 expression activate/deactivate record"));

                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out _);

                Assert.That(success, Is.True);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void TryExportTimelineAsset_ValueProviderRecording_ExportsValueProviderTrackWithNamesFromReferenceModel()
        {
            ExportFixture fixture = ExportFixture.Create(valueProvider: true);
            var model = new GameObject("RecToTimelineExportReferenceModel");
            var mesh = new Mesh { name = "RecToTimelineExportReferenceMesh", vertices = new[] { Vector3.zero, Vector3.right, Vector3.up } };
            mesh.AddBlendShapeFrame("eyeBlinkLeft", 100f, new Vector3[3], null, null);
            mesh.AddBlendShapeFrame("jawOpen", 100f, new Vector3[3], null, null);
            new GameObject("Face").transform.SetParent(model.transform, false);
            model.transform.GetChild(0).gameObject.AddComponent<SkinnedMeshRenderer>().sharedMesh = mesh;

            try
            {
                fixture.Profile.ReferenceModel = model;

                bool success = RecToTimelineExporter.TryExportTimelineAsset(
                    fixture.RecordingAbsolutePath,
                    fixture.Profile,
                    fixture.TimelinePath,
                    out RecToTimelineExporter.ExportResult result);

                Assert.That(success, Is.True);
                ChannelDetection detection = default;
                foreach (ChannelDetection candidate in result.ChannelDetections)
                {
                    if (candidate.SourceId == "ifm")
                    {
                        detection = candidate;
                    }
                }

                Assert.That(detection.Kind, Is.EqualTo(FacialValueChannelKind.ValueProvider));
                Assert.That(detection.Reason, Is.EqualTo(ChannelDetectionReason.ValueProviderNamed));
                Assert.That(detection.AxisCount, Is.EqualTo(2));

                FacialValueTrack track = null;
                foreach (TrackAsset candidate in result.Timeline.GetRootTracks())
                {
                    if (candidate is FacialValueTrack valueTrack && valueTrack.ChannelSubId == "ifm")
                    {
                        track = valueTrack;
                    }
                }

                Assert.That(track, Is.Not.Null);
                var clip = (FacialValueClip)ToArray(track.GetClips())[0].asset;
                Assert.That(clip.BlendShapeNames, Is.EqualTo(new[] { "eyeBlinkLeft", "jawOpen" }));

                var derivation = Hidano.FacialControl.Timeline.Domain.Services.TimelineChannelDeriver.Derive(
                    TimelineAssetScanner.Scan(result.Timeline).Tracks, TimelineProfileSource.Resolve(fixture.Profile));
                Assert.That(derivation.InvalidChannelSubIds, Is.Empty);
                var channel = derivation.Channels[0];
                foreach (var candidate in derivation.Channels)
                {
                    if (candidate.ChannelSubId == "ifm")
                    {
                        channel = candidate;
                    }
                }

                Assert.That(channel.Kind, Is.EqualTo(FacialValueChannelKind.ValueProvider));
                Assert.That(channel.BlendShapeBindings.Count, Is.EqualTo(2));
                Assert.That(channel.BlendShapeBindings[1].Name, Is.EqualTo("jawOpen"));
                Assert.That(channel.BlendShapeBindings[1].RecordedIndex, Is.EqualTo(1));
            }
            finally
            {
                fixture.Profile.ReferenceModel = null;
                UnityEngine.Object.DestroyImmediate(model);
                UnityEngine.Object.DestroyImmediate(mesh);
                fixture.Dispose();
            }
        }

        private static RecTimeline CreateSingleTriggerRecording(string expressionId)
        {
            return new RecTimeline(
                RecBaselineState.Empty,
                new[]
                {
                    RecEvent.CreateTriggerOn(0.10d, 0, 0),
                    RecEvent.CreateTriggerOff(0.60d, 0, 0),
                },
                new[]
                {
                    "input:trigger",
                },
                new[]
                {
                    expressionId,
                },
                1.0d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    Array.Empty<float>(),
                });
        }

        private static TimelineAsset CreateExistingTimeline(string timelinePath)
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, timelinePath);

            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "Existing");
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialExpressionClip)clip.asset).ExpressionId = "existing";

            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssetIfDirty(timeline);
            AssetDatabase.Refresh();
            return timeline;
        }

        private static FacialTimelineBakeAsset FindBakeAsset(string timelinePath)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(timelinePath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is FacialTimelineBakeAsset bakeAsset)
                {
                    return bakeAsset;
                }
            }

            return null;
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

        private sealed class ExportFixture : IDisposable
        {
            private readonly string _folderPath;

            private ExportFixture(
                string folderPath,
                FacialCharacterProfileSO profile,
                string timelinePath,
                string recordingAbsolutePath)
            {
                _folderPath = folderPath;
                Profile = profile;
                TimelinePath = timelinePath;
                RecordingAbsolutePath = recordingAbsolutePath;
            }

            public FacialCharacterProfileSO Profile { get; }

            public string TimelinePath { get; }

            public string RecordingAbsolutePath { get; }

            public static ExportFixture Create(
                bool overlappingTriggers = false,
                bool includeWeightRecords = false,
                bool valueProvider = false)
            {
                string folderName = "RecToTimelineExportWorkflowTests_" + Guid.NewGuid().ToString("N");
                string folderPath = "Assets/" + folderName;
                AssetDatabase.CreateFolder("Assets", folderName);

                string profilePath = folderPath + "/Profile.asset";
                string timelinePath = folderPath + "/ExportedTimeline.playable";
                string recordingPath = folderPath + "/recording.rec";

                var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
                profile.SchemaVersion = "1.0.0";
                profile.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                });
                profile.Expressions.Add(new ExpressionSerializable
                {
                    id = "smile",
                    name = "Smile",
                    layer = "emotion",
                    transitionDuration = 0.1f,
                    blendShapeValues = new List<BlendShapeMappingSerializable>
                    {
                        new BlendShapeMappingSerializable
                        {
                            name = "Smile",
                            value = 1f,
                        },
                    },
                });
                profile.Expressions.Add(new ExpressionSerializable
                {
                    id = "wink",
                    name = "Wink",
                    layer = "emotion",
                    transitionDuration = 0.1f,
                    blendShapeValues = new List<BlendShapeMappingSerializable>
                    {
                        new BlendShapeMappingSerializable
                        {
                            name = "Wink",
                            value = 1f,
                        },
                    },
                });
                AssetDatabase.CreateAsset(profile, profilePath);

                RecTimeline timeline = valueProvider
                    ? CreateValueProviderRecordingTimeline()
                    : includeWeightRecords
                    ? CreateRecordingTimelineWithWeights()
                    : overlappingTriggers
                        ? CreateOverlappingRecordingTimeline()
                        : CreateRecordingTimeline();
                File.WriteAllBytes(recordingPath, RecBinaryFormat.Serialize(timeline, 123L));
                AssetDatabase.Refresh();

                return new ExportFixture(
                    folderPath,
                    profile,
                    timelinePath,
                    Path.GetFullPath(recordingPath));
            }

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(_folderPath);
                AssetDatabase.Refresh();
            }

            /// <summary>
            /// 同一レイヤーで smile と wink が重なる（子レーンが生成される）記録と、2 軸のアナログ記録。
            /// </summary>
            private static RecTimeline CreateOverlappingRecordingTimeline()
            {
                return new RecTimeline(
                    RecBaselineState.Empty,
                    new[]
                    {
                        RecEvent.CreateTriggerOn(0.10d, 0, 0),
                        RecEvent.CreateTriggerOn(0.20d, 0, 1),
                        RecEvent.CreateAnalogSample(0.25d, 1, 2),
                        RecEvent.CreateTriggerOff(0.40d, 0, 1),
                        RecEvent.CreateTriggerOff(0.60d, 0, 0),
                    },
                    new[]
                    {
                        "input:trigger",
                        "live:gaze",
                    },
                    new[]
                    {
                        "smile",
                        "wink",
                    },
                    1.0d,
                    new IReadOnlyList<float>[]
                    {
                        Array.Empty<float>(),
                        Array.Empty<float>(),
                        new[] { 0.25f, -0.25f },
                        Array.Empty<float>(),
                        Array.Empty<float>(),
                    });
            }

            /// <summary>
            /// weight の基準エントリ（レイヤー / 入力源）と時刻付き weight を含む、rec-weight-coverage 以降の形式の記録。
            /// </summary>
            private static RecTimeline CreateRecordingTimelineWithWeights()
            {
                var baseline = new RecBaselineState(
                    null,
                    null,
                    null,
                    null,
                    new[] { new LayerWeightEntry("emotion", 1f) },
                    new[] { new InputSourceWeightEntry("emotion", "input:trigger", 1f) });
                return new RecTimeline(
                    baseline,
                    new[]
                    {
                        RecEvent.CreateTriggerOn(0.10d, 0, 0),
                        RecEvent.CreateLayerWeightSample(0.15d, 0),
                        RecEvent.CreateAnalogSample(0.20d, 1, 2),
                        RecEvent.CreateInputSourceWeightSample(0.30d, 0, 0),
                        RecEvent.CreateTriggerOff(0.60d, 0, 0),
                    },
                    new[]
                    {
                        "input:trigger",
                        "live:gaze",
                    },
                    new[]
                    {
                        "smile",
                    },
                    new[]
                    {
                        "emotion",
                    },
                    1.0d,
                    new IReadOnlyList<float>[]
                    {
                        Array.Empty<float>(),
                        new[] { 0.5f },
                        new[] { 0.25f, -0.25f },
                        new[] { 0.75f },
                        Array.Empty<float>(),
                    });
            }

            /// <summary>値提供型（slug だけの source id <c>ifm</c>、BlendShape 2 個）の基準と時刻付きレコード。</summary>
            private static RecTimeline CreateValueProviderRecordingTimeline()
            {
                var baseline = new RecBaselineState(
                    null,
                    null,
                    new[] { new RecBaselineState.ValueProviderEntry("ifm", true, new byte[] { 0b0000_0011 }, new[] { 0.1f, 0.2f }) },
                    null);
                return new RecTimeline(
                    baseline,
                    new[]
                    {
                        RecEvent.CreateValueProviderSample(
                            0.5d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasValues, 2, 0),
                    },
                    new[] { "ifm" },
                    Array.Empty<string>(),
                    1.0d,
                    new IReadOnlyList<float>[] { new[] { 0.6f, 0.7f } },
                    new IReadOnlyList<byte>[] { Array.Empty<byte>() });
            }

            private static RecTimeline CreateRecordingTimeline()
            {
                return new RecTimeline(
                    RecBaselineState.Empty,
                    new[]
                    {
                        RecEvent.CreateTriggerOn(0.10d, 0, 0),
                        RecEvent.CreateValueProviderSample(0.15d, 1, RecValueProviderFlags.None, 0, 0),
                        RecEvent.CreateExpressionActivate(0.17d, 0, 0),
                        RecEvent.CreateAnalogSample(0.20d, 1, 2),
                        RecEvent.CreateExpressionDeactivate(0.25d, 0, 0),
                        RecEvent.CreateTriggerOff(0.60d, 0, 0),
                    },
                    new[]
                    {
                        "input:trigger",
                        "live:gaze",
                    },
                    new[]
                    {
                        "smile",
                    },
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
            }
        }
    }
}
