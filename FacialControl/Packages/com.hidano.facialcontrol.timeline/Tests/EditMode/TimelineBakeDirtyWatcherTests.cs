using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Timeline;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [MediumTest]
    public sealed class TimelineBakeDirtyWatcherTests : SizedTestFixture
    {
        [Test]
        public void ProcessTrackedAssetPathsNow_WhenTimelineSaved_RebakesStaleTimeline()
        {
            TestAssetFixture fixture = CreateFixture();

            try
            {
                string originalHash = fixture.Bake.SourceHashHex;
                ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
                EditorUtility.SetDirty(fixture.Timeline);

                TimelineBakeDirtyWatcher.ProcessTrackedAssetPathsNow(new[] { fixture.TimelinePath });

                Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile.BuildFallbackProfile(), fixture.Bake), Is.False);
                Assert.That(fixture.Bake.SourceHashHex, Is.Not.EqualTo(originalHash));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void ProcessTrackedAssetPathsNow_WhenProfileSaved_RebakesDependentTimeline()
        {
            TestAssetFixture fixture = CreateFixture();

            try
            {
                string originalHash = fixture.Bake.SourceHashHex;
                fixture.Profile.Expressions[0].transitionDuration = 0.35f;
                EditorUtility.SetDirty(fixture.Profile);

                TimelineBakeDirtyWatcher.ProcessTrackedAssetPathsNow(new[] { fixture.ProfilePath });

                Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile.BuildFallbackProfile(), fixture.Bake), Is.False);
                Assert.That(fixture.Bake.SourceHashHex, Is.Not.EqualTo(originalHash));
            }
            finally
            {
                fixture.Dispose();
            }
        }

        [Test]
        public void HandleEnteredEditModeNow_WhenMismatchSessionRecorded_RebakesAndDisplaysDialog()
        {
            TestAssetFixture fixture = CreateFixture();
            var host = new GameObject("TimelineHost");
            var receiver = host.AddComponent<FacialTimelineReceiver>();
            var controller = host.AddComponent<FacialController>();
            var registry = new FakeInputSourceRegistry();
            string dialogTitle = null;
            string dialogMessage = null;
            Action<string, string, string> originalDialog = TimelineBakeDirtyWatcher.DisplayDialog;

            try
            {
                controller.CharacterSO = fixture.Profile;
                receiver.BakeAsset = fixture.Bake;
                receiver.Configure(
                    fixture.Profile.BuildFallbackProfile(),
                    registry,
                    Array.Empty<(string layer, TimelineExpressionStateSink sink)>(),
                    Array.Empty<(string sub, TimelineBakedValueSink sink)>(),
                    Array.Empty<(string sub, TimelineAnalogInputSource sink)>(),
                    Array.Empty<(string sub, TimelineGazeInputSource sink, string takeoverSourceId)>());

                ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
                EditorUtility.SetDirty(fixture.Timeline);

                TimelineBakeDirtyWatcher.DisplayDialog = (title, message, ok) =>
                {
                    dialogTitle = title;
                    dialogMessage = message;
                };

                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(@"\[FacialTimelineReceiver\].*hash mismatch"));
                receiver.BeginPlaybackSession(fixture.Profile.BuildFallbackProfile(), fixture.Timeline);

                RepairRunResult result = TimelineBakeDirtyWatcher.HandleEnteredEditModeNow();

                Assert.That(result.Succeeded, Is.EqualTo(1));
                Assert.That(result.Failed, Is.EqualTo(0));
                Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile.BuildFallbackProfile(), fixture.Bake), Is.False);
                Assert.That(dialogTitle, Is.EqualTo("Timeline Bake Repair"));
                StringAssert.Contains("Auto-rebaked 1 timeline bake asset(s)", dialogMessage);
            }
            finally
            {
                TimelineBakeDirtyWatcher.DisplayDialog = originalDialog;
                if (receiver != null)
                {
                    UnityEngine.Object.DestroyImmediate(host);
                }

                fixture.Dispose();
            }
        }

        private static TestAssetFixture CreateFixture()
        {
            string folderName = "TimelineBakeDirtyWatcherTests_" + Guid.NewGuid().ToString("N");
            string folderPath = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            string profilePath = folderPath + "/Profile.asset";
            string timelinePath = folderPath + "/Timeline.playable";

            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            profile.SchemaVersion = "1.0.0";
            profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "Expressions",
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
            });
            profile.Expressions.Add(new ExpressionSerializable
            {
                id = "smile",
                name = "Smile",
                layer = "Expressions",
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
                id = "smile-2",
                name = "Smile 2",
                layer = "Expressions",
                transitionDuration = 0.2f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable
                    {
                        name = "Smile",
                        value = 0.5f,
                    },
                },
            });
            AssetDatabase.CreateAsset(profile, profilePath);

            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, timelinePath);

            FacialExpressionTrack expressionTrack = timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");
            TimelineClip expressionClip = expressionTrack.CreateClip<FacialExpressionClip>();
            expressionClip.start = 0d;
            expressionClip.duration = 1d;
            ((FacialExpressionClip)expressionClip.asset).ExpressionId = "smile";

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.name = "FacialTimelineBake";
            AssetDatabase.AddObjectToAsset(bake, timeline);
            TimelineBakeService.UpdateBakeAsset(timeline, profile, bake);

            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(bake);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            return new TestAssetFixture(folderPath, profilePath, timelinePath, profile, timeline, expressionClip, bake);
        }

        private sealed class TestAssetFixture : IDisposable
        {
            public TestAssetFixture(
                string folderPath,
                string profilePath,
                string timelinePath,
                FacialCharacterProfileSO profile,
                TimelineAsset timeline,
                TimelineClip expressionClip,
                FacialTimelineBakeAsset bake)
            {
                FolderPath = folderPath;
                ProfilePath = profilePath;
                TimelinePath = timelinePath;
                Profile = profile;
                Timeline = timeline;
                ExpressionClip = expressionClip;
                Bake = bake;
            }

            public string FolderPath { get; }

            public string ProfilePath { get; }

            public string TimelinePath { get; }

            public FacialCharacterProfileSO Profile { get; }

            public TimelineAsset Timeline { get; }

            public TimelineClip ExpressionClip { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(FolderPath);
                AssetDatabase.Refresh();
            }
        }

        private sealed class FakeInputSourceRegistry : IInputSourceRegistry
        {
            private readonly Dictionary<string, IInputSource> _entries =
                new Dictionary<string, IInputSource>(StringComparer.Ordinal);
            private readonly List<string> _registeredIds = new List<string>();

            public IReadOnlyList<string> RegisteredIds => _registeredIds;

            public void Register(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Replace(AdapterSlug slug, IInputSource source)
            {
                RegisterInternal(slug.Value, source);
            }

            public void Register(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Replace(AdapterSlug slug, string sub, IInputSource source)
            {
                RegisterInternal(Compose(slug, sub), source);
            }

            public void Unregister(AdapterSlug slug)
            {
                _entries.Remove(slug.Value);
            }

            public void Unregister(AdapterSlug slug, string sub)
            {
                _entries.Remove(Compose(slug, sub));
            }

            public bool TryResolve(string layerInputSourceId, out IInputSource source)
            {
                return _entries.TryGetValue(layerInputSourceId, out source);
            }

            public void Subscribe(string id, Action<IInputSource> handler)
            {
            }

            private void RegisterInternal(string id, IInputSource source)
            {
                _entries[id] = source;
                if (!_registeredIds.Contains(id))
                {
                    _registeredIds.Add(id);
                }
            }

            private static string Compose(AdapterSlug slug, string sub)
            {
                return string.IsNullOrEmpty(sub) ? slug.Value : slug.Value + ":" + sub;
            }
        }
    }
}
