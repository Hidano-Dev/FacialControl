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
        public void TryExportTimelineAsset_NewAsset_CreatesTimelineBakeAndBindings()
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
                    out RecToTimelineExporter.ExportResult result,
                    director: director,
                    receiver: receiver);

                Assert.That(success, Is.True);
                Assert.That(result.Success, Is.True);
                Assert.That(result.Timeline, Is.Not.Null);
                Assert.That(result.BakeAsset, Is.Not.Null);
                Assert.That(AssetDatabase.LoadAssetAtPath<TimelineAsset>(fixture.TimelinePath), Is.Not.Null);
                Assert.That(receiver.BakeAsset, Is.SameAs(result.BakeAsset));
                Assert.That(AssetDatabase.GetAssetPath(director.playableAsset), Is.EqualTo(fixture.TimelinePath));

                foreach (TrackAsset track in result.Timeline.GetOutputTracks())
                {
                    Assert.That(director.GetGenericBinding(track), Is.SameAs(receiver));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
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

            public static ExportFixture Create(bool overlappingTriggers = false)
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

                RecTimeline timeline = overlappingTriggers
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
