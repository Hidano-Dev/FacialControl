using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.Json;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineProfileSource"/> が Runtime と同じ <c>LoadProfile()</c> の結果をキャッシュ付きで返し、
    /// キャッシュキー（profile.json の有無 / 最終更新時刻・SO のダーティ状態）と明示無効化で更新されることを固定する。
    /// AssetDatabase と StreamingAssets の profile.json を使うため Medium。
    /// </summary>
    [MediumTest]
    public sealed class TimelineProfileSourceTests : SizedTestFixture
    {
        private string _folderPath;
        private string _assetName;
        private FacialCharacterProfileSO _profileAsset;
        private readonly List<GameObject> _hosts = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            string guid = Guid.NewGuid().ToString("N");
            string folderName = "TimelineProfileSourceTests_" + guid;
            _folderPath = "Assets/" + folderName;
            _assetName = "TimelineProfileSourceTestProfile_" + guid;
            AssetDatabase.CreateFolder("Assets", folderName);

            _profileAsset = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            _profileAsset.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
            });
            _profileAsset.Expressions.Add(CreateExpression("smile", 1f));
            AssetDatabase.CreateAsset(_profileAsset, _folderPath + "/" + _assetName + ".asset");
            AssetDatabase.SaveAssets();
            TimelineProfileSource.InvalidateAll();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _hosts.Count; i++)
            {
                if (_hosts[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_hosts[i]);
                }
            }

            _hosts.Clear();
            FacialControllerRendererOwnership.Clear();
            TimelineProfileSource.InvalidateAll();

            if (AssetDatabase.IsValidFolder(_folderPath))
            {
                AssetDatabase.DeleteAsset(_folderPath);
            }

            string exportDir = Path.GetDirectoryName(ProfileJsonPath);
            if (!string.IsNullOrEmpty(exportDir) && Directory.Exists(exportDir))
            {
                Directory.Delete(exportDir, recursive: true);
            }

            if (!string.IsNullOrEmpty(exportDir) && File.Exists(exportDir + ".meta"))
            {
                File.Delete(exportDir + ".meta");
            }
        }

        private string ProfileJsonPath => FacialCharacterProfileSO.GetStreamingAssetsProfilePath(_assetName);

        [Test]
        public void Resolve_WithoutProfileJson_MatchesLoadProfileContentHash()
        {
            Assert.That(File.Exists(ProfileJsonPath), Is.False, "前提: profile.json が無い");

            FacialProfile resolved = TimelineProfileSource.Resolve(_profileAsset);

            Assert.That(ContentHash(resolved), Is.EqualTo(ContentHash(_profileAsset.LoadProfile())));
            Assert.That(ContentHash(resolved), Is.EqualTo(ContentHash(_profileAsset.BuildFallbackProfile())));
        }

        [Test]
        public void Resolve_WithProfileJson_UsesJsonAndMatchesLoadProfileContentHash()
        {
            WriteProfileJson(CreateJsonProfile("wink"), new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            FacialProfile resolved = TimelineProfileSource.Resolve(_profileAsset);

            Assert.That(resolved.FindExpressionById("wink").HasValue, Is.True, "JSON 側の Profile が使われる");
            Assert.That(ContentHash(resolved), Is.EqualTo(ContentHash(_profileAsset.LoadProfile())));
            Assert.That(ContentHash(resolved), Is.Not.EqualTo(ContentHash(_profileAsset.BuildFallbackProfile())));
        }

        [Test]
        public void Resolve_SameCacheKey_ReturnsCachedProfileWithoutReloading()
        {
            var writeTime = new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            WriteProfileJson(CreateJsonProfile("wink"), writeTime);
            TimelineProfileSource.Resolve(_profileAsset);

            // 内容だけ変えて最終更新時刻を元に戻す（キャッシュキーが同じ）。
            WriteProfileJson(CreateJsonProfile("blink"), writeTime);

            FacialProfile resolved = TimelineProfileSource.Resolve(_profileAsset);

            Assert.That(resolved.FindExpressionById("wink").HasValue, Is.True);
            Assert.That(resolved.FindExpressionById("blink").HasValue, Is.False);
        }

        [Test]
        public void Resolve_WhenProfileJsonLastWriteTimeChanges_ReturnsUpdatedProfile()
        {
            WriteProfileJson(CreateJsonProfile("wink"), new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            TimelineProfileSource.Resolve(_profileAsset);

            WriteProfileJson(CreateJsonProfile("blink"), new DateTime(2002, 1, 1, 0, 0, 0, DateTimeKind.Utc));

            FacialProfile resolved = TimelineProfileSource.Resolve(_profileAsset);

            Assert.That(resolved.FindExpressionById("blink").HasValue, Is.True);
            Assert.That(ContentHash(resolved), Is.EqualTo(ContentHash(_profileAsset.LoadProfile())));
        }

        [Test]
        public void Resolve_WhenProfileJsonDeleted_FallsBackToProfileAsset()
        {
            WriteProfileJson(CreateJsonProfile("wink"), new DateTime(2001, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            TimelineProfileSource.Resolve(_profileAsset);

            File.Delete(ProfileJsonPath);

            FacialProfile resolved = TimelineProfileSource.Resolve(_profileAsset);

            Assert.That(resolved.FindExpressionById("wink").HasValue, Is.False);
            Assert.That(ContentHash(resolved), Is.EqualTo(ContentHash(_profileAsset.BuildFallbackProfile())));
        }

        [Test]
        public void InvalidateCache_AfterUntrackedEdit_ReturnsUpdatedProfile()
        {
            TimelineProfileSource.Resolve(_profileAsset);

            // SetDirty を伴わない編集はキャッシュキーに現れない。
            _profileAsset.Expressions.Add(CreateExpression("frown", 0.5f));
            Assert.That(EditorUtility.IsDirty(_profileAsset), Is.False, "前提: SO はダーティでない");
            Assert.That(
                TimelineProfileSource.Resolve(_profileAsset).FindExpressionById("frown").HasValue,
                Is.False,
                "無効化前はキャッシュが返る");

            TimelineProfileSource.InvalidateCache(_profileAsset);

            Assert.That(TimelineProfileSource.Resolve(_profileAsset).FindExpressionById("frown").HasValue, Is.True);
        }

        [Test]
        public void Resolve_WhenProfileAssetIsDirty_ReflectsEdits()
        {
            TimelineProfileSource.Resolve(_profileAsset);

            _profileAsset.Expressions.Add(CreateExpression("frown", 0.5f));
            EditorUtility.SetDirty(_profileAsset);

            Assert.That(TimelineProfileSource.Resolve(_profileAsset).FindExpressionById("frown").HasValue, Is.True);
        }

        [Test]
        public void OnWillSaveAssets_ProfileAssetPath_InvalidatesCache()
        {
            TimelineProfileSource.Resolve(_profileAsset);
            _profileAsset.Expressions.Add(CreateExpression("frown", 0.5f));

            string[] paths = { AssetDatabase.GetAssetPath(_profileAsset) };
            string[] returned = TimelineProfileSource.OnWillSaveAssets(paths);

            Assert.That(returned, Is.SameAs(paths));
            Assert.That(TimelineProfileSource.Resolve(_profileAsset).FindExpressionById("frown").HasValue, Is.True);
        }

        [Test]
        public void TryResolveForTimeline_BakeRecordsProfileGuid_ReturnsThatProfileAsset()
        {
            TimelineAsset timeline = CreateTimelineAsset();
            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.name = "FacialTimelineBake";
            bake.ProfileAssetGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(_profileAsset));
            AssetDatabase.AddObjectToAsset(bake, timeline);
            AssetDatabase.SaveAssets();

            bool resolved = TimelineProfileSource.TryResolveForTimeline(
                timeline,
                out FacialCharacterProfileSO profileAsset,
                out FacialProfile profile);

            Assert.That(resolved, Is.True);
            Assert.That(profileAsset, Is.SameAs(_profileAsset));
            Assert.That(ContentHash(profile), Is.EqualTo(ContentHash(_profileAsset.LoadProfile())));
        }

        [Test]
        public void TryResolveForTimeline_DirectorBindsReceiver_ReturnsControllerProfileAsset()
        {
            TimelineAsset timeline = CreateTimelineAsset();
            var host = new GameObject("TimelineProfileSourceHost");
            _hosts.Add(host);
            host.AddComponent<Animator>();
            var controller = host.AddComponent<FacialController>();
            controller.CharacterSO = _profileAsset;
            var receiver = host.AddComponent<FacialTimelineReceiver>();
            var director = host.AddComponent<PlayableDirector>();
            director.playableAsset = timeline;
            foreach (TrackAsset track in timeline.GetOutputTracks())
            {
                director.SetGenericBinding(track, receiver);
            }

            bool resolved = TimelineProfileSource.TryResolveForTimeline(
                timeline,
                out FacialCharacterProfileSO profileAsset,
                out FacialProfile profile);

            Assert.That(resolved, Is.True);
            Assert.That(profileAsset, Is.SameAs(_profileAsset));
            Assert.That(ContentHash(profile), Is.EqualTo(ContentHash(_profileAsset.LoadProfile())));
        }

        [Test]
        public void TryResolveForTimeline_NoBakeAndNoDirector_ReturnsFalse()
        {
            TimelineAsset timeline = CreateTimelineAsset();

            bool resolved = TimelineProfileSource.TryResolveForTimeline(
                timeline,
                out FacialCharacterProfileSO profileAsset,
                out _);

            Assert.That(resolved, Is.False);
            Assert.That(profileAsset, Is.Null);
        }

        private TimelineAsset CreateTimelineAsset()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            AssetDatabase.CreateAsset(timeline, _folderPath + "/Timeline.playable");
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, "emotion");
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialExpressionClip)clip.asset).ExpressionId = "smile";
            EditorUtility.SetDirty(timeline);
            AssetDatabase.SaveAssets();
            return timeline;
        }

        private FacialProfile CreateJsonProfile(string extraExpressionId)
        {
            var source = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            try
            {
                source.Layers.Add(new LayerDefinitionSerializable
                {
                    name = "emotion",
                    priority = 0,
                    exclusionMode = ExclusionMode.LastWins,
                });
                source.Expressions.Add(CreateExpression("smile", 1f));
                source.Expressions.Add(CreateExpression(extraExpressionId, 0.75f));
                return source.BuildFallbackProfile();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(source);
            }
        }

        private void WriteProfileJson(FacialProfile profile, DateTime lastWriteTimeUtc)
        {
            string path = ProfileJsonPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, new SystemTextJsonParser().SerializeProfile(profile));
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
        }

        private string ContentHash(FacialProfile profile)
        {
            return FacialTimelineHashCalculator.ComputeProfileContentHashHex(
                profile,
                FacialTimelineHashCalculator.ToGazeChannelArray(_profileAsset.GazeChannels));
        }

        private static ExpressionSerializable CreateExpression(string id, float value)
        {
            return new ExpressionSerializable
            {
                id = id,
                name = id,
                layer = "emotion",
                transitionDuration = 0.1f,
                blendShapeValues = new List<BlendShapeMappingSerializable>
                {
                    new BlendShapeMappingSerializable
                    {
                        name = "Smile",
                        value = value,
                    },
                },
            };
        }
    }
}
