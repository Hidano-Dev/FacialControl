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
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    public sealed class RecToTimelineExportWorkflowTests
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

            public static ExportFixture Create()
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
                AssetDatabase.CreateAsset(profile, profilePath);

                RecTimeline timeline = CreateRecordingTimeline();
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

            private static RecTimeline CreateRecordingTimeline()
            {
                return new RecTimeline(
                    RecBaselineState.Empty,
                    new[]
                    {
                        RecEvent.CreateTriggerOn(0.10d, 0, 0),
                        RecEvent.CreateAnalogSample(0.20d, 1, 2),
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
                        new[] { 0.25f, -0.25f },
                        Array.Empty<float>(),
                    });
            }
        }
    }
}
