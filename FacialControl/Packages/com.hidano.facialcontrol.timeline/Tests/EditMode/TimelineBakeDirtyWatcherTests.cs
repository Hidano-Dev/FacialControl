using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.AutoExport;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
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
    [MediumTest]
    public sealed class TimelineBakeDirtyWatcherTests : SizedTestFixture
    {
        private const string LogPrefix = "[TimelineBakeDirtyWatcher]";

        private readonly List<IDisposable> _disposables = new List<IDisposable>();
        private readonly List<UnityEngine.Object> _transient = new List<UnityEngine.Object>();
        private readonly List<FacialCharacterProfileSO> _exported = new List<FacialCharacterProfileSO>();
        private Func<bool> _originalPlayProbe;

        [SetUp]
        public void SetUp()
        {
            _exported.Clear();
            _originalPlayProbe = TimelineBakeDirtyWatcher.IsPlayModeTransition;
            TimelineBakeDirtyWatcher.IsPlayModeTransition = () => false;
            TimelineBakeDirtyWatcher.ClearPendingRepairs();
            FacialCharacterProfileAutoExporter.Exported += OnExported;
            TimelineProfileSource.InvalidateAll();
            FacialControllerRendererOwnership.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FacialCharacterProfileAutoExporter.Exported -= OnExported;
            TimelineBakeDirtyWatcher.IsPlayModeTransition = _originalPlayProbe;
            TimelineBakeDirtyWatcher.ClearPendingRepairs();

            for (int i = _transient.Count - 1; i >= 0; i--)
            {
                if (_transient[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_transient[i]);
                }
            }

            _transient.Clear();

            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                _disposables[i].Dispose();
            }

            _disposables.Clear();
            FacialControllerRendererOwnership.Clear();
            TimelineProfileSource.InvalidateAll();
        }

        private void OnExported(FacialCharacterProfileSO so)
        {
            _exported.Add(so);
        }

        [Test]
        public void ProcessTrackedAssetPathsNow_WhenTimelineSaved_RebakesStaleTimeline()
        {
            TestAssetFixture fixture = CreateFixture();

            string originalHash = fixture.Bake.SourceHashHex;
            ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
            EditorUtility.SetDirty(fixture.Timeline);

            TimelineBakeDirtyWatcher.ProcessTrackedAssetPathsNow(new[] { fixture.TimelinePath });

            Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile, fixture.Bake), Is.EqualTo(BakeStaleReason.None));
            Assert.That(fixture.Bake.SourceHashHex, Is.Not.EqualTo(originalHash));
        }

        [Test]
        public void ProcessTrackedAssetPathsNow_WhenProfileSaved_RebakesDependentTimeline()
        {
            TestAssetFixture fixture = CreateFixture();

            string originalHash = fixture.Bake.SourceHashHex;
            fixture.Profile.Expressions[0].transitionDuration = 0.35f;
            EditorUtility.SetDirty(fixture.Profile);

            TimelineBakeDirtyWatcher.ProcessTrackedAssetPathsNow(new[] { fixture.ProfilePath });

            Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile, fixture.Bake), Is.EqualTo(BakeStaleReason.None));
            Assert.That(fixture.Bake.SourceHashHex, Is.Not.EqualTo(originalHash));
        }

        [Test]
        public void TryRepairPendingSessionIssuesNow_WhenSessionDetectedBakeStale_RepairsSilentlyWithOneInfoLine()
        {
            TestAssetFixture fixture = CreateFixture();
            SceneHost host = CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: false);
            FacialProfile profile = fixture.Profile.BuildFallbackProfile();
            host.Controller.InitializeWithProfile(profile);
            host.Receiver.AttachBinding(new TimelineBindingContext(
                AdapterSlug.Parse("timeline"),
                profile,
                Array.Empty<string>(),
                new InputSourceRegistry(),
                host.Controller,
                enabled: true));

            ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
            EditorUtility.SetDirty(fixture.Timeline);

            host.Receiver.BeginPlaybackSession(fixture.Timeline, host.Director);
            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(host.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeStale), Is.True);

            RepairRunResult result;
            using (var logs = new WatcherLogCounter())
            {
                result = TimelineBakeDirtyWatcher.TryRepairPendingSessionIssuesNow();

                Assert.That(logs.Infos, Is.EqualTo(1), "無言修復の Info は 1 行");
                Assert.That(logs.Warnings, Is.EqualTo(0));
            }

            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(result.Failed, Is.EqualTo(0));
            Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile, fixture.Bake), Is.EqualTo(BakeStaleReason.None));
        }

        [Test]
        public void TryRepairPendingSessionIssuesNow_NothingToRepair_LogsNothing()
        {
            TestAssetFixture fixture = CreateFixture();
            CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: true);

            using (var logs = new WatcherLogCounter())
            {
                RepairRunResult result = TimelineBakeDirtyWatcher.TryRepairPendingSessionIssuesNow();

                Assert.That(result.Attempted, Is.EqualTo(0));
                Assert.That(logs.Infos + logs.Warnings, Is.EqualTo(0));
            }
        }

        [Test]
        public void RebakeNow_StaleTimeline_WritesSameBakeToAllHoldersIncludingChildren()
        {
            TestAssetFixture fixture = CreateFixture(withChildAndValueTracks: true);
            ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
            EditorUtility.SetDirty(fixture.Timeline);
            TimelineReceiverTestHost.AssignBakeToAllTracks(fixture.Timeline, null);

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(
                fixture.Timeline, fixture.Profile, out FacialTimelineBakeAsset bake, out string failure);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.Rebaked), failure);
            Assert.That(bake, Is.SameAs(fixture.Bake));
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(fixture.Timeline).TrackAssets;
            Assert.That(tracks.Count, Is.EqualTo(3), "前提: root Expression + 子 + Value");
            for (int i = 0; i < tracks.Count; i++)
            {
                Assert.That(((IFacialTimelineBakeHolder)tracks[i]).Bake, Is.SameAs(fixture.Bake), tracks[i].name);
            }

            Assert.That(FacialTimelineBakeLocator.Locate(fixture.Timeline, null).Status, Is.EqualTo(BakeLocateStatus.Found));
        }

        [Test]
        public void RebakeNow_HashMatchesButReferencesInconsistent_ReturnsReferencesRepaired()
        {
            TestAssetFixture fixture = CreateFixture(withChildAndValueTracks: true);
            IReadOnlyList<TrackAsset> tracks = TimelineAssetScanner.Scan(fixture.Timeline).TrackAssets;
            ((IFacialTimelineBakeHolder)tracks[1]).Bake = null;
            Assert.That(FacialTimelineBakeLocator.Locate(fixture.Timeline, null).Status, Is.EqualTo(BakeLocateStatus.Conflict));
            string hashBefore = fixture.Bake.SourceHashHex;

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(
                fixture.Timeline, fixture.Profile, out FacialTimelineBakeAsset bake, out _);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.ReferencesRepaired));
            Assert.That(bake, Is.SameAs(fixture.Bake));
            Assert.That(fixture.Bake.SourceHashHex, Is.EqualTo(hashBefore), "焼き直していない");
            Assert.That(FacialTimelineBakeLocator.Locate(fixture.Timeline, null).Status, Is.EqualTo(BakeLocateStatus.Found));
        }

        [Test]
        public void RebakeNow_FreshAndConsistent_ReturnsNoChange()
        {
            TestAssetFixture fixture = CreateFixture();

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(
                fixture.Timeline, fixture.Profile, out FacialTimelineBakeAsset bake, out _);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.NoChange));
            Assert.That(bake, Is.SameAs(fixture.Bake));
        }

        [Test]
        public void RebakeNow_TimelineOnly_ResolvesProfileFromBakeGuid()
        {
            TestAssetFixture fixture = CreateFixture();
            ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
            EditorUtility.SetDirty(fixture.Timeline);

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(fixture.Timeline, out _, out string failure);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.Rebaked), failure);
            Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile, fixture.Bake), Is.EqualTo(BakeStaleReason.None));
        }

        [Test]
        public void RebakeNow_UnresolvableProfile_ReturnsFailedWithReason()
        {
            var timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            _transient.Add(timeline);
            timeline.CreateTrack<FacialExpressionTrack>(null, "Expressions");

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(timeline, out _, out string failure);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.Failed));
            Assert.That(failure, Is.Not.Empty);
        }

        [Test]
        public void RebakeNow_ReceiverWithoutOverride_LeavesBakeAssetNull()
        {
            TestAssetFixture fixture = CreateFixture();
            SceneHost host = CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: true);
            ((FacialExpressionClip)fixture.ExpressionClip.asset).ExpressionId = "smile-2";
            EditorUtility.SetDirty(fixture.Timeline);

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(fixture.Timeline, fixture.Profile, out _, out _);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.Rebaked));
            Assert.That(host.Receiver.BakeAsset, Is.Null, "上書き欄は自動で書かない");
        }

        [Test]
        public void RebakeNow_ReceiverWithExplicitOverride_FollowsNewBakeUndoably()
        {
            TestAssetFixture fixture = CreateFixture();
            var oldBake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            oldBake.name = "OldBake";
            AssetDatabase.AddObjectToAsset(oldBake, fixture.Timeline);
            AssetDatabase.SaveAssets();
            SceneHost host = CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: true);
            host.Receiver.BakeAsset = oldBake;
            Undo.IncrementCurrentGroup();

            RebakeOutcome outcome = TimelineBakeDirtyWatcher.RebakeNow(fixture.Timeline, fixture.Profile, out _, out _);

            Assert.That(outcome, Is.EqualTo(RebakeOutcome.ReferencesRepaired));
            Assert.That(host.Receiver.BakeAsset, Is.SameAs(fixture.Bake));

            Undo.PerformUndo();

            Assert.That(host.Receiver.BakeAsset, Is.SameAs(oldBake), "Receiver 参照の更新は Undo できる");
        }

        [Test]
        public void ProcessTrackedAssetPathsNow_DuringPlayMode_DefersUntilEnteredEditMode()
        {
            TestAssetFixture fixture = CreateFixture();
            SceneHost host = CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: true);
            string originalHash = fixture.Bake.SourceHashHex;
            fixture.Profile.Expressions[0].transitionDuration = 0.35f;
            EditorUtility.SetDirty(fixture.Profile);

            TimelineBakeDirtyWatcher.IsPlayModeTransition = () => true;
            TimelineBakeDirtyWatcher.OnWillSaveAssets(new[] { fixture.ProfilePath, fixture.TimelinePath });
            TimelineBakeDirtyWatcher.ProcessTrackedAssetPathsNow(new[] { fixture.ProfilePath, fixture.TimelinePath });

            Assert.That(fixture.Bake.SourceHashHex, Is.EqualTo(originalHash), "Play 中は再ベイクしない");
            Assert.That(host.Receiver.BakeAsset, Is.Null, "Play 中は Receiver を書き換えない");

            TimelineBakeDirtyWatcher.IsPlayModeTransition = () => false;
            RepairRunResult result = TimelineBakeDirtyWatcher.TryRepairPendingSessionIssuesNow();

            Assert.That(result.Succeeded, Is.EqualTo(1));
            Assert.That(fixture.Bake.SourceHashHex, Is.Not.EqualTo(originalHash));
            Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile, fixture.Bake), Is.EqualTo(BakeStaleReason.None));
            Assert.That(host.Receiver.BakeAsset, Is.Null);
        }

        [Test]
        public void ProcessOpenSceneTimelinesNow_AutoExportProfileChanged_BakeMatchesProfileJsonAndSessionIsProfileMatched()
        {
            TestAssetFixture fixture = CreateFixture();
            SceneHost host = CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: true);
            fixture.Profile.Expressions[0].transitionDuration = 0.45f;
            EditorUtility.SetDirty(fixture.Profile);

            TimelineBakeDirtyWatcher.ProcessOpenSceneTimelinesNow();

            Assert.That(File.Exists(fixture.ProfileJsonPath), Is.True, "AutoExport の冪等入口が profile.json を書く");
            FacialProfile loaded = fixture.Profile.LoadProfile();
            Assert.That(fixture.Bake.ProfileContentHashHex, Is.EqualTo(ContentHash(fixture.Profile, loaded)));
            AssertSessionProfileMatched(host, fixture, loaded);
        }

        [TestCase(true, TestName = "ExitingEditMode_ExportAllThenSerial_SameSnapshotAndSingleWrite")]
        [TestCase(false, TestName = "ExitingEditMode_SerialThenExportAll_SameSnapshotAndSingleWrite")]
        public void ExitingEditMode_EitherOrder_SameSnapshotAndSingleWrite(bool exportAllFirst)
        {
            TestAssetFixture fixture = CreateFixture();
            CreateSceneHost(fixture.Profile, fixture.Timeline, bindDirector: true);
            fixture.Profile.Expressions[0].transitionDuration = 0.55f;
            EditorUtility.SetDirty(fixture.Profile);

            RunExitingEditMode(exportAllFirst);

            Assert.That(CountExported(fixture.Profile), Is.EqualTo(1), "完了イベントは合計 1 回");
            string json = File.ReadAllText(fixture.ProfileJsonPath);
            string expectedHash = ContentHash(fixture.Profile, fixture.Profile.LoadProfile());
            Assert.That(fixture.Bake.ProfileContentHashHex, Is.EqualTo(expectedHash));
            Assert.That(TimelineBakeService.IsStale(fixture.Timeline, fixture.Profile, fixture.Bake), Is.EqualTo(BakeStaleReason.None));

            var past = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(fixture.ProfileJsonPath, past);
            string sourceHash = fixture.Bake.SourceHashHex;

            RunExitingEditMode(exportAllFirst);

            Assert.That(File.GetLastWriteTimeUtc(fixture.ProfileJsonPath), Is.EqualTo(past), "2 回目は書かない");
            Assert.That(File.ReadAllText(fixture.ProfileJsonPath), Is.EqualTo(json));
            Assert.That(fixture.Bake.SourceHashHex, Is.EqualTo(sourceHash));
            Assert.That(CountExported(fixture.Profile), Is.EqualTo(1));
        }

        [Test]
        public void ProcessOpenSceneTimelinesNow_AutoExportDisabledProfile_NoJsonAndFallbackMatches()
        {
            TestAssetFixture fixture = CreateFixture();
            // CharacterAssetName（= name）が空の非永続 SO は AutoExport の対象外。
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            _transient.Add(profile);
            profile.name = string.Empty;
            CopyProfileContent(fixture.Profile, profile);
            TimelineBakeService.UpdateBakeAsset(fixture.Timeline, profile, fixture.Bake);
            SceneHost host = CreateSceneHost(profile, fixture.Timeline, bindDirector: true);
            profile.Expressions[0].transitionDuration = 0.65f;

            TimelineBakeDirtyWatcher.ProcessOpenSceneTimelinesNow();

            Assert.That(FacialCharacterProfileSO.GetStreamingAssetsProfilePath(profile.CharacterAssetName), Is.Null.Or.Empty);
            Assert.That(CountExported(profile), Is.EqualTo(0));
            FacialProfile loaded = profile.LoadProfile();
            Assert.That(fixture.Bake.ProfileContentHashHex, Is.EqualTo(ContentHash(profile, loaded)));
            AssertSessionProfileMatched(host, fixture, loaded);
        }

        private void RunExitingEditMode(bool exportAllFirst)
        {
            if (exportAllFirst)
            {
                FacialCharacterProfileAutoExporter.ExportAll("test");
                TimelineBakeDirtyWatcher.ProcessOpenSceneTimelinesNow();
            }
            else
            {
                TimelineBakeDirtyWatcher.ProcessOpenSceneTimelinesNow();
                FacialCharacterProfileAutoExporter.ExportAll("test");
            }
        }

        private int CountExported(FacialCharacterProfileSO profile)
        {
            int count = 0;
            for (int i = 0; i < _exported.Count; i++)
            {
                if (ReferenceEquals(_exported[i], profile))
                {
                    count++;
                }
            }

            return count;
        }

        private static void AssertSessionProfileMatched(SceneHost host, TestAssetFixture fixture, FacialProfile loaded)
        {
            host.Controller.InitializeWithProfile(loaded);
            host.Receiver.AttachBinding(new TimelineBindingContext(
                AdapterSlug.Parse("timeline"),
                loaded,
                Array.Empty<string>(),
                new InputSourceRegistry(),
                host.Controller,
                enabled: true));

            host.Receiver.BeginPlaybackSession(fixture.Timeline, host.Director);

            Assert.That(host.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(host.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True);
            Assert.That(host.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.False);
            host.Receiver.ReleaseAll();
        }

        private static string ContentHash(FacialCharacterProfileSO so, FacialProfile profile)
        {
            return FacialTimelineHashCalculator.ComputeProfileContentHashHex(
                profile,
                FacialTimelineHashCalculator.ToGazeChannelArray(so.GazeChannels));
        }

        private SceneHost CreateSceneHost(FacialCharacterProfileSO profile, TimelineAsset timeline, bool bindDirector)
        {
            var root = new GameObject("TimelineBakeDirtyWatcherHost");
            _transient.Add(root);
            root.AddComponent<Animator>();
            var controller = root.AddComponent<FacialController>();
            controller.CharacterSO = profile;
            var receiver = root.AddComponent<FacialTimelineReceiver>();
            var director = root.AddComponent<PlayableDirector>();
            if (bindDirector)
            {
                director.playableAsset = timeline;
                foreach (TrackAsset track in timeline.GetOutputTracks())
                {
                    director.SetGenericBinding(track, receiver);
                }
            }

            return new SceneHost(controller, receiver, director);
        }

        private static void CopyProfileContent(FacialCharacterProfileSO source, FacialCharacterProfileSO target)
        {
            target.SchemaVersion = source.SchemaVersion;
            for (int i = 0; i < source.Layers.Count; i++)
            {
                LayerDefinitionSerializable layer = source.Layers[i];
                target.Layers.Add(new LayerDefinitionSerializable
                {
                    name = layer.name,
                    priority = layer.priority,
                    exclusionMode = layer.exclusionMode,
                });
            }

            for (int i = 0; i < source.Expressions.Count; i++)
            {
                ExpressionSerializable expression = source.Expressions[i];
                target.Expressions.Add(new ExpressionSerializable
                {
                    id = expression.id,
                    name = expression.name,
                    layer = expression.layer,
                    transitionDuration = expression.transitionDuration,
                    blendShapeValues = new List<BlendShapeMappingSerializable>(expression.blendShapeValues),
                });
            }
        }

        private TestAssetFixture CreateFixture(bool withChildAndValueTracks = false)
        {
            string guid = Guid.NewGuid().ToString("N");
            string folderName = "TimelineBakeDirtyWatcherTests_" + guid;
            string folderPath = "Assets/" + folderName;
            AssetDatabase.CreateFolder("Assets", folderName);

            string profileName = "DirtyWatcherProfile_" + guid;
            string profilePath = folderPath + "/" + profileName + ".asset";
            string timelinePath = folderPath + "/Timeline.playable";

            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            // profile.json の strict schema（"1.0"）で読み戻せる値にする（AutoExport 経路のテストで JSON を読むため）。
            profile.SchemaVersion = "1.0";
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

            if (withChildAndValueTracks)
            {
                FacialExpressionTrack lane = timeline.CreateTrack<FacialExpressionTrack>(expressionTrack, "Expressions Lane 1");
                TimelineClip laneClip = lane.CreateClip<FacialExpressionClip>();
                laneClip.start = 1d;
                laneClip.duration = 1d;
                ((FacialExpressionClip)laneClip.asset).ExpressionId = "smile";
                timeline.CreateTrack<FacialValueTrack>(null, "osc:lt");
            }

            var bake = ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            bake.name = "FacialTimelineBake";
            AssetDatabase.AddObjectToAsset(bake, timeline);
            TimelineBakeService.UpdateBakeAsset(timeline, profile, bake);

            EditorUtility.SetDirty(profile);
            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(bake);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var fixture = new TestAssetFixture(folderPath, profilePath, timelinePath, profile, timeline, expressionClip, bake);
            _disposables.Add(fixture);
            return fixture;
        }

        private sealed class SceneHost
        {
            public SceneHost(FacialController controller, FacialTimelineReceiver receiver, PlayableDirector director)
            {
                Controller = controller;
                Receiver = receiver;
                Director = director;
            }

            public FacialController Controller { get; }

            public FacialTimelineReceiver Receiver { get; }

            public PlayableDirector Director { get; }
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
                ProfileJsonPath = FacialCharacterProfileSO.GetStreamingAssetsProfilePath(profile.CharacterAssetName);
            }

            public string FolderPath { get; }

            public string ProfilePath { get; }

            public string TimelinePath { get; }

            public string ProfileJsonPath { get; }

            public FacialCharacterProfileSO Profile { get; }

            public TimelineAsset Timeline { get; }

            public TimelineClip ExpressionClip { get; }

            public FacialTimelineBakeAsset Bake { get; }

            public void Dispose()
            {
                AssetDatabase.DeleteAsset(FolderPath);

                string exportDir = string.IsNullOrEmpty(ProfileJsonPath) ? null : Path.GetDirectoryName(ProfileJsonPath);
                if (!string.IsNullOrEmpty(exportDir) && Directory.Exists(exportDir))
                {
                    Directory.Delete(exportDir, recursive: true);
                }

                if (!string.IsNullOrEmpty(exportDir) && File.Exists(exportDir + ".meta"))
                {
                    File.Delete(exportDir + ".meta");
                }

                AssetDatabase.Refresh();
            }
        }

        /// <summary>DirtyWatcher の Console 出力を種別ごとに数える。</summary>
        private sealed class WatcherLogCounter : IDisposable
        {
            public WatcherLogCounter()
            {
                UnityEngine.Application.logMessageReceived += OnLog;
            }

            public int Infos { get; private set; }

            public int Warnings { get; private set; }

            public void Dispose()
            {
                UnityEngine.Application.logMessageReceived -= OnLog;
            }

            private void OnLog(string condition, string stackTrace, LogType type)
            {
                if (condition == null || !condition.Contains(LogPrefix))
                {
                    return;
                }

                if (type == LogType.Log)
                {
                    Infos++;
                }
                else if (type == LogType.Warning)
                {
                    Warnings++;
                }
            }
        }
    }
}
