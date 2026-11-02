using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.Diagnostics;
using Hidano.FacialControl.Timeline.Adapters.Scanning;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using Hidano.FacialControl.Timeline.Domain.Services;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="TimelineDiagnosticsEvaluator"/> の静的診断を、GameObject + Receiver + Director を組んで診断コードの値で検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class TimelineDiagnosticsEvaluatorTests : SizedTestFixture
    {
        private const string EmotionLayer = "emotion";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
            TearDownSavedAssets();
        }

        // ================================================================
        // 問題なし
        // ================================================================

        [Test]
        public void EvaluateStatic_AllConfigured_OverallIsOkAndHealthyStatesAreKept()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Ok), Describe(diagnostics));
            Assert.That(diagnostics.HasErrors, Is.False);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeFresh), Is.True, "問題なしの状態値が保持される");
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True);
        }

        // ================================================================
        // Director
        // ================================================================

        [Test]
        public void EvaluateStatic_NoDirector_ReportsDirectorMissing()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(
                s.Receiver, diagnostics, s.BuildContext(director: null, directorStatus: DirectorResolveStatus.NotFound, timeline: null));

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.DirectorMissing), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.DirectorMissing), Is.EqualTo(TimelineDiagnosticSeverity.Error));
        }

        [Test]
        public void EvaluateStatic_DirectorAmbiguous_ReportsDirectorAmbiguous()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(
                s.Receiver, diagnostics, s.BuildContext(director: null, directorStatus: DirectorResolveStatus.Ambiguous, timeline: null));

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.DirectorAmbiguous), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.DirectorMissing), Is.False);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.DirectorAmbiguous), Is.EqualTo(TimelineDiagnosticSeverity.Warning),
                "同じキャラを複数の Director が Bind するのは正当な構成なので Warning（HID-408）");
        }

        [Test]
        public void EvaluateStatic_DirectorWithoutTimeline_ReportsTimelineNotBound()
        {
            Scenario s = CreateScenario();
            s.Director.playableAsset = null;
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext(timeline: null));

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TimelineNotBound), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.TimelineNotBound), Is.EqualTo(TimelineDiagnosticSeverity.Error));
        }

        // ================================================================
        // TrackBinding
        // ================================================================

        [Test]
        public void EvaluateStatic_FacialTrackOutsideCharacterTrack_ReportsLegacyTrackStructureError()
        {
            Scenario s = CreateScenario();
            s.Timeline.CreateTrack<FacialValueTrack>(null, "legacy");
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyTrackStructure, "legacy"), Is.True, Describe(diagnostics));
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.LegacyTrackStructure), Is.EqualTo(TimelineDiagnosticSeverity.Error));
        }

        [Test]
        public void EvaluateStatic_TwoCharacterTracks_ReportsMultipleCharacterTracksError()
        {
            Scenario s = CreateScenario();
            s.Timeline.CreateTrack<FacialCharacterTrack>(null, "Second");
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.MultipleCharacterTracks), Is.True, Describe(diagnostics));
        }

        [Test]
        public void EvaluateStatic_MutedLegacyTrackAndMutedSecondCharacterTrack_AreNotCounted()
        {
            // ミュート中（親階層のミュートを含む）のトラックは再生されないので、構造の Error でセッションを止めない（HID-408 レビュー指摘 2）。
            Scenario s = CreateScenario();
            s.Timeline.CreateTrack<FacialValueTrack>(null, "muted-legacy").muted = true;
            var group = s.Timeline.CreateTrack<GroupTrack>(null, "muted-group");
            group.muted = true;
            s.Timeline.CreateTrack<FacialValueTrack>(group, "legacy-in-muted-group");
            s.Timeline.CreateTrack<FacialCharacterTrack>(null, "muted-character").muted = true;
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyTrackStructure), Is.False, Describe(diagnostics));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.MultipleCharacterTracks), Is.False, Describe(diagnostics));
        }

        [Test]
        public void EvaluateTrackStructureArea_AllUnderOneCharacterTrack_ReturnsFalseAndClearsArea()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(TimelineDiagnosticArea.TrackBinding, new[]
            {
                new TimelineDiagnosticItem(TimelineDiagnosticArea.TrackBinding, TimelineDiagnosticCode.LegacyTrackStructure,
                    TimelineDiagnosticSeverity.Error, "stale", string.Empty),
            });

            bool hasError = TimelineDiagnosticsEvaluator.EvaluateTrackStructureArea(diagnostics, s.Timeline);

            Assert.That(hasError, Is.False);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyTrackStructure), Is.False, "再評価で古い項目を消す");
        }

        // ================================================================
        // Bake
        // ================================================================

        [Test]
        public void EvaluateStatic_AllTrackReferencesNull_ReportsLegacyExportError()
        {
            Scenario s = CreateScenario();
            s.Track.Bake = null;
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeLegacyExport), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.BakeLegacyExport), Is.EqualTo(TimelineDiagnosticSeverity.Error));
        }

        [Test]
        public void EvaluateStatic_PartialTrackReference_ReportsReferenceConflictError()
        {
            Scenario s = CreateScenario();
            var second = s.Character.CreateChildTrack<FacialExpressionTrack>("other");
            second.Bake = null;
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.BakeReferenceConflict), Is.EqualTo(TimelineDiagnosticSeverity.Error));
        }

        [Test]
        public void EvaluateStatic_NoFacialTracks_ReportsBakeMissingWarning()
        {
            Scenario s = CreateScenario();
            s.Timeline.DeleteTrack(s.Track);
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeMissing), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.BakeMissing), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
        }

        [Test]
        public void EvaluateStatic_SourceHashDiffersWithMatchingProfile_ReportsBakeStaleWarning()
        {
            Scenario s = CreateScenario();
            s.Bake.SourceHashHex = "0000000000000000";
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeStale), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeFresh), Is.False);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.BakeStale), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
        }

        [Test]
        public void EvaluateStatic_OverrideBakeDiffersFromTrackReference_ReportsOverrideUsedAndDiffers()
        {
            Scenario s = CreateScenario();
            FacialTimelineBakeAsset overrideBake = s.CreateFreshBake();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(
                s.Receiver, diagnostics, s.BuildContext(bake: FacialTimelineBakeLocator.Locate(s.Timeline, overrideBake)));

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeOverrideUsed), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeOverrideDiffers), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.BakeOverrideDiffers), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
        }

        // ================================================================
        // Profile
        // ================================================================

        [Test]
        public void EvaluateStatic_ProfileContentHashDiffers_ReportsProfileMismatchAsWarning()
        {
            Scenario s = CreateScenario();
            s.Bake.ProfileContentHashHex = "deadbeefdeadbeef";
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.ProfileMismatch), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(diagnostics.HasErrors, Is.False, "Profile 不一致は停止理由にしない");
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeStale), Is.False,
                "Profile 不一致のときは BakeStale を重ねない");
        }

        [Test]
        public void EvaluateStatic_EmptyProfileContentHash_ReportsProfileMismatch()
        {
            Scenario s = CreateScenario();
            s.Bake.ProfileContentHashHex = string.Empty;
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True);
        }

        // ================================================================
        // Profile の Adapter Bindings（HID-408 で Timeline binding は不要になった）
        // ================================================================

        [Test]
        public void EvaluateStatic_ProfileWithoutTimelineBinding_HasNoErrors()
        {
            Scenario s = CreateScenario();
            s.ProfileSO.WritableAdapterBindings.Clear();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.HasErrors, Is.False, "Timeline binding が無くても再生できる。" + Describe(diagnostics));
        }

        // ================================================================
        // LayerMatch
        // ================================================================

        [Test]
        public void EvaluateStatic_TrackNameNotInProfile_ReportsTrackLayerUnmatched()
        {
            Scenario s = CreateScenario();
            s.Track.name = "Emotion Typo";
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched, "Emotion Typo"), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.TrackLayerUnmatched), Is.EqualTo(TimelineDiagnosticSeverity.Warning));
        }

        // ================================================================
        // Placement
        // ================================================================

        [Test]
        public void EvaluateStatic_NoController_ReportsControllerMissing()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext(controller: null, useController: false));

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ControllerMissing), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.ControllerMissing), Is.EqualTo(TimelineDiagnosticSeverity.Error));
        }

        [Test]
        public void EvaluateStatic_ControllerNotInitialized_ReportsInfoOnly()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(s.Controller.IsInitialized, Is.False, "fixture: Edit では controller は未初期化");
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ControllerNotInitialized), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.ControllerNotInitialized), Is.EqualTo(TimelineDiagnosticSeverity.Info));
        }

        // ================================================================
        // 領域単位の置換
        // ================================================================

        [Test]
        public void EvaluateStatic_ReevaluatedAfterFix_RemovesStaleItemsAndKeepsSessionAreas()
        {
            Scenario s = CreateScenario();
            var diagnostics = new FacialTimelineDiagnostics();
            var sessionItem = new TimelineDiagnosticItem(
                TimelineDiagnosticArea.LayerConnection,
                TimelineDiagnosticCode.LayerConnected,
                TimelineDiagnosticSeverity.Info,
                EmotionLayer,
                string.Empty);
            diagnostics.ReplaceArea(TimelineDiagnosticArea.LayerConnection, new[] { sessionItem });
            s.Track.name = "Emotion Typo";
            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched), Is.True);
            int revision = diagnostics.Revision;

            s.Track.name = EmotionLayer;
            TimelineDiagnosticsEvaluator.EvaluateStatic(s.Receiver, diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched), Is.False);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnected, EmotionLayer), Is.True,
                "静的評価はセッション側の領域を消さない");
            Assert.That(diagnostics.Revision, Is.GreaterThan(revision));
        }

        [Test]
        public void EvaluateBakeAndProfileAreas_ReplacesOnlyBakeAndProfileAreas()
        {
            Scenario s = CreateScenario();
            s.Bake.ProfileContentHashHex = "deadbeefdeadbeef";
            var diagnostics = new FacialTimelineDiagnostics();
            var placement = new TimelineDiagnosticItem(
                TimelineDiagnosticArea.Placement,
                TimelineDiagnosticCode.ControllerMissing,
                TimelineDiagnosticSeverity.Error,
                string.Empty,
                string.Empty);
            diagnostics.ReplaceArea(TimelineDiagnosticArea.Placement, new[] { placement });

            TimelineDiagnosticsEvaluator.EvaluateBakeAndProfileAreas(diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.ControllerMissing), Is.True, "他領域は触らない");
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.DirectorMissing), Is.False);
        }

        [Test]
        public void EvaluateLayerMatchArea_TrackNameNotInProfile_ReplacesOnlyLayerMatchArea()
        {
            Scenario s = CreateScenario();
            s.Track.name = "Emotion Typo";
            var diagnostics = new FacialTimelineDiagnostics();

            TimelineDiagnosticsEvaluator.EvaluateLayerMatchArea(diagnostics, s.BuildContext());

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched, "Emotion Typo"), Is.True);
            Assert.That(diagnostics.Items, Has.Count.EqualTo(1));
        }

        // ================================================================
        // Edit 側（Receiver Inspector の評価）
        // ================================================================

        [Test]
        public void EditEvaluate_ReferenceConflictOnSavedTimeline_MarksWatcherPending()
        {
            Scenario s = CreateScenario();
            SaveTimeline(s);
            var second = s.Character.CreateChildTrack<FacialExpressionTrack>("other");
            second.Bake = null;
            TimelineEditChangeWatcher watcher = CreateWatcher();

            ReceiverEditEvaluation result = FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: true);

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict), Is.True, Describe(s.Receiver.Diagnostics));
            Assert.That(watcher.IsPending(s.Timeline), Is.True);
            Assert.That(result.AutoRebakePending, Is.True);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.UnsavedTimeline), Is.False);
        }

        [Test]
        public void EditEvaluate_ProfileMismatchOnSavedTimeline_MarksWatcherPending()
        {
            Scenario s = CreateScenario();
            SaveTimeline(s);
            s.Bake.ProfileContentHashHex = "deadbeefdeadbeef";
            TimelineEditChangeWatcher watcher = CreateWatcher();

            ReceiverEditEvaluation result = FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: true);

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True, Describe(s.Receiver.Diagnostics));
            Assert.That(watcher.IsPending(s.Timeline), Is.True);
            Assert.That(result.AutoRebakePending, Is.True);
        }

        [Test]
        public void EditEvaluate_ConflictOnUnsavedTimeline_ReportsUnsavedTimelineAndStaysNotPending()
        {
            Scenario s = CreateScenario();
            var second = s.Character.CreateChildTrack<FacialExpressionTrack>("other");
            second.Bake = null;
            TimelineEditChangeWatcher watcher = CreateWatcher();

            ReceiverEditEvaluation result = FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: true);

            Assert.That(watcher.IsPending(s.Timeline), Is.False);
            Assert.That(result.AutoRebakePending, Is.False);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.UnsavedTimeline), Is.True, Describe(s.Receiver.Diagnostics));
            Assert.That(SeverityOf(s.Receiver.Diagnostics, TimelineDiagnosticCode.UnsavedTimeline), Is.EqualTo(TimelineDiagnosticSeverity.Info));
        }

        [Test]
        public void EditEvaluate_WithGate_RequestsOnceUntilDiagnosticResolves()
        {
            Scenario s = CreateScenario();
            SaveTimeline(s);
            var second = s.Character.CreateChildTrack<FacialExpressionTrack>("other");
            second.Bake = null;
            TimelineEditChangeWatcher watcher = CreateWatcher();
            var gate = new AutoRebakeRequestGate();

            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, true, gate);
            watcher.FlushNow();
            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, true, gate);
            Assert.That(watcher.IsPending(s.Timeline), Is.False, "解消しないまま再評価しても再要求しない");

            second.Bake = s.Bake;
            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, true, gate);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict), Is.False);
            watcher.FlushNow();
            Assert.That(watcher.IsPending(s.Timeline), Is.False);
            second.Bake = null;
            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, true, gate);

            Assert.That(watcher.IsPending(s.Timeline), Is.True, "解消後に再発したら再び要求する");
        }

        [Test]
        public void EditEvaluate_RequestAutoRebakeFalse_DoesNotMarkDirty()
        {
            Scenario s = CreateScenario();
            SaveTimeline(s);
            s.Bake.ProfileContentHashHex = "deadbeefdeadbeef";
            TimelineEditChangeWatcher watcher = CreateWatcher();

            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: false);

            Assert.That(watcher.IsPending(s.Timeline), Is.False);
        }

        [Test]
        public void EditEvaluate_LegacyDeclarations_MapsToLayerConnectionArea()
        {
            Scenario s = CreateScenario();
            s.ProfileSO.Layers.Add(new LayerDefinitionSerializable
            {
                name = EmotionLayer,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "timeline:emotion:state", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "timeline:emotion", weight = 0.5f },
                },
            });
            TimelineEditChangeWatcher watcher = CreateWatcher();

            ReceiverEditEvaluation result = FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: true);

            FacialTimelineDiagnostics diagnostics = s.Receiver.Diagnostics;
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration, EmotionLayer + " / timeline:emotion:state"), Is.True, Describe(diagnostics));
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.LegacyStateDeclaration), Is.EqualTo(TimelineDiagnosticSeverity.Error));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnectionSkippedDeclared, EmotionLayer), Is.True);
            Assert.That(SeverityOf(diagnostics, TimelineDiagnosticCode.LayerConnectionSkippedDeclared), Is.EqualTo(TimelineDiagnosticSeverity.Info));
            Assert.That(result.HasLegacyStateDeclarations, Is.True);
        }

        [Test]
        public void EditEvaluate_GivenDirectorWhileTwoDirectorsBindController_UsesGivenDirectorWithoutStoringIt()
        {
            // 同じ controller を 2 つの Director が Bind しているとシーン走査では Ambiguous。Inspector が開いている Director を
            // 評価のたびに渡し、共有の Receiver には保存しない（HID-408 レビュー指摘 3）。
            Scenario s = CreateScenario();
            var otherHost = new GameObject("OtherDirectorHost");
            _created.Add(otherHost);
            var otherDirector = otherHost.AddComponent<PlayableDirector>();
            otherDirector.playableAsset = s.Timeline;
            otherDirector.SetGenericBinding(s.Character, s.Controller);
            TimelineEditChangeWatcher watcher = CreateWatcher();

            ReceiverEditEvaluation result = FacialTimelineReceiverEditEvaluator.Evaluate(
                s.Receiver, watcher, requestAutoRebake: false, gate: null, director: s.Director);

            Assert.That(result.Director, Is.SameAs(s.Director));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.DirectorAmbiguous), Is.False, Describe(s.Receiver.Diagnostics));
            Assert.That(s.Receiver.DirectorOverride, Is.Null, "共有の Receiver に保存しない");

            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: false);

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.DirectorAmbiguous), Is.True,
                "Director を渡さない評価はシーン走査（2 つあるので Ambiguous）");
        }

        [Test]
        public void EditEvaluate_ResolvesDirectorTimelineAndProfile()
        {
            Scenario s = CreateScenario();
            TimelineEditChangeWatcher watcher = CreateWatcher();

            ReceiverEditEvaluation result = FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: true);

            Assert.That(result.Director, Is.SameAs(s.Director));
            Assert.That(result.Timeline, Is.SameAs(s.Timeline));
            Assert.That(result.ProfileAsset, Is.SameAs(s.ProfileSO));
        }

        // ================================================================
        // Edit 側はトラックの binding を書かない（自動 Bind の廃止。HID-408）
        // ================================================================

        [Test]
        public void EditEvaluate_UnboundCharacterTrack_DoesNotWriteBindingOrDirtyDirector()
        {
            Scenario s = CreateScenario();
            s.Director.ClearGenericBinding(s.Character);
            int dirtyBefore = UnityEditor.EditorUtility.GetDirtyCount(s.Director);
            s.Receiver.DirectorOverride = s.Director;
            TimelineEditChangeWatcher watcher = CreateWatcher();

            FacialTimelineReceiverEditEvaluator.Evaluate(s.Receiver, watcher, requestAutoRebake: true);

            Assert.That(s.Director.GetGenericBinding(s.Character), Is.Null, "親トラックの binding を自動で書かない");
            Assert.That(s.Director.GetGenericBinding(s.Track), Is.Null, "子トラックにも書かない");
            Assert.That(UnityEditor.EditorUtility.GetDirtyCount(s.Director), Is.EqualTo(dirtyBefore));
        }

        private string _savedFolder;

        private void TearDownSavedAssets()
        {
            if (!string.IsNullOrEmpty(_savedFolder) && UnityEditor.AssetDatabase.IsValidFolder(_savedFolder))
            {
                UnityEditor.AssetDatabase.DeleteAsset(_savedFolder);
            }

            _savedFolder = null;
        }

        private void SaveTimeline(Scenario s)
        {
            string folderName = "TimelineDiagnosticsEvaluatorTests_" + System.Guid.NewGuid().ToString("N");
            UnityEditor.AssetDatabase.CreateFolder("Assets", folderName);
            _savedFolder = "Assets/" + folderName;
            _created.Remove(s.Timeline);
            UnityEditor.AssetDatabase.CreateAsset(s.Timeline, _savedFolder + "/Timeline.playable");
        }

        private static TimelineEditChangeWatcher CreateWatcher()
        {
            return new TimelineEditChangeWatcher(new NoopRebakeExecutor(), () => 0d, null, null)
            {
                IsPlayModeTransition = () => false,
                RefreshTimelineWindow = null,
            };
        }

        private sealed class NoopRebakeExecutor : IRebakeExecutor
        {
            public RebakeOutcome Rebake(TimelineAsset timeline, out FacialTimelineBakeAsset bake, out string failureReason)
            {
                bake = null;
                failureReason = string.Empty;
                return RebakeOutcome.NoChange;
            }
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private sealed class Scenario
        {
            public FacialTimelineReceiver Receiver;
            public FacialController Controller;
            public PlayableDirector Director;
            public TimelineAsset Timeline;
            public FacialExpressionTrack Track;
            public FacialTimelineBakeAsset Bake;
            public TestProfileSO ProfileSO;
            public FacialCharacterTrack Character;
            public FacialProfile Profile;
            public System.Func<FacialTimelineBakeAsset> BakeFactory;

            public FacialTimelineBakeAsset CreateFreshBake() => BakeFactory();

            public TimelineStaticEvaluationContext BuildContext(
                PlayableDirector director = null,
                DirectorResolveStatus directorStatus = DirectorResolveStatus.SameObject,
                TimelineAsset timeline = null,
                FacialController controller = null,
                bool useController = true,
                FacialCharacterProfileSO profileSource = null,
                bool useProfileSource = true,
                BakeLocateResult? bake = null)
            {
                bool directorGiven = director != null || directorStatus != DirectorResolveStatus.SameObject;
                PlayableDirector resolvedDirector = directorGiven ? director : Director;
                TimelineAsset resolvedTimeline = directorGiven
                    ? timeline
                    : timeline ?? (Director.playableAsset as TimelineAsset);
                FacialController resolvedController = useController ? (controller != null ? controller : Controller) : null;
                FacialCharacterProfileSO resolvedSource = useProfileSource ? (profileSource != null ? profileSource : ProfileSO) : null;
                BakeLocateResult located = bake ?? FacialTimelineBakeLocator.Locate(resolvedTimeline, null);
                TimelineDerivation derivation = TimelineChannelDeriver.Derive(
                    TimelineAssetScanner.Scan(resolvedTimeline).Tracks, Profile);

                return new TimelineStaticEvaluationContext(
                    director: resolvedDirector,
                    directorStatus: directorGiven ? directorStatus : DirectorResolveStatus.SameObject,
                    timeline: resolvedTimeline,
                    controller: resolvedController,
                    profileSource: resolvedSource,
                    profile: Profile,
                    hasProfile: true,
                    gazeChannels: ProfileSO.GazeChannels,
                    bake: located,
                    derivation: derivation);
            }
        }

        private Scenario CreateScenario()
        {
            var host = new GameObject("CharacterHost");
            _created.Add(host);
            host.AddComponent<Animator>();
            var controller = host.AddComponent<FacialController>();
            var receiver = host.AddComponent<FacialTimelineReceiver>();
            var director = host.AddComponent<PlayableDirector>();

            var timeline = UnityEngine.ScriptableObject.CreateInstance<TimelineAsset>();
            _created.Add(timeline);
            var character = timeline.CreateTrack<FacialCharacterTrack>(null, "Facial Character");
            var track = character.CreateChildTrack<FacialExpressionTrack>(EmotionLayer);
            director.playableAsset = timeline;
            director.SetGenericBinding(character, controller);

            var profileSO = UnityEngine.ScriptableObject.CreateInstance<TestProfileSO>();
            _created.Add(profileSO);
            controller.CharacterSO = profileSO;

            FacialProfile profile = CreateProfile();
            var scenario = new Scenario
            {
                Receiver = receiver,
                Controller = controller,
                Director = director,
                Timeline = timeline,
                Track = track,
                ProfileSO = profileSO,
                Character = character,
                Profile = profile,
            };
            scenario.BakeFactory = () =>
            {
                var created = UnityEngine.ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
                _created.Add(created);
                GazeChannel[] gaze = FacialTimelineHashCalculator.ToGazeChannelArray(profileSO.GazeChannels);
                created.ProfileContentHashHex = FacialTimelineHashCalculator.ComputeProfileContentHashHex(profile, gaze);
                created.SourceHashHex = FacialTimelineHashCalculator.ComputeHashHex(timeline, profile, gaze, created.SampleRate);
                return created;
            };
            scenario.Bake = scenario.BakeFactory();
            track.Bake = scenario.Bake;
            return scenario;
        }

        private static FacialProfile CreateProfile()
        {
            var layers = new[] { new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins) };
            var expressions = new[]
            {
                new Expression(
                    "smile", "Smile", EmotionLayer, 0.05f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("smile", 1f) }),
            };
            return new FacialProfile("1.0", layers, expressions);
        }

        private static TimelineDiagnosticSeverity SeverityOf(FacialTimelineDiagnostics diagnostics, TimelineDiagnosticCode code)
        {
            foreach (TimelineDiagnosticItem item in diagnostics.Items)
            {
                if (item.Code == code)
                {
                    return item.Severity;
                }
            }

            Assert.Fail($"診断 {code} が記録されていない。{Describe(diagnostics)}");
            return default;
        }

        private static string SubjectOf(FacialTimelineDiagnostics diagnostics, TimelineDiagnosticCode code)
        {
            foreach (TimelineDiagnosticItem item in diagnostics.Items)
            {
                if (item.Code == code)
                {
                    return item.Subject;
                }
            }

            Assert.Fail($"診断 {code} が記録されていない。{Describe(diagnostics)}");
            return null;
        }

        private static string Describe(FacialTimelineDiagnostics diagnostics)
        {
            var parts = new List<string>();
            foreach (TimelineDiagnosticItem item in diagnostics.Items)
            {
                parts.Add($"{item.Area}/{item.Code}/{item.Severity}/{item.Subject}");
            }

            return "items=[" + string.Join(", ", parts) + "]";
        }

        private sealed class TestProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
        }
    }
}
