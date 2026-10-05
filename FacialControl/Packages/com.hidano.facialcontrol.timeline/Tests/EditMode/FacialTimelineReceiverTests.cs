using System;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Clips;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Tracks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// <see cref="FacialTimelineReceiver"/> の再生セッション（接続 → Begin → Active / Pending / Failed → ReleaseAll）を
    /// 状態値と診断コードで検証する。
    /// </summary>
    [MediumTest]
    public sealed class FacialTimelineReceiverTests : SizedTestFixture
    {
        private const string EmotionLayer = "emotion";
        private const string ValueId = "timeline:emotion";
        private const string StateId = "timeline:emotion:state";
        private static readonly string[] BlendShapes = { "smile", "frown" };

        private readonly List<IDisposable> _disposables = new List<IDisposable>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _disposables.Count - 1; i >= 0; i--)
            {
                _disposables[i].Dispose();
            }

            _disposables.Clear();
        }

        // ================================================================
        // 接続 → Begin → Active
        // ================================================================

        [Test]
        public void BeginPlaybackSession_TrackNameMatchesProfileLayer_BecomesActiveAndConnectsLayer()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), Describe(s.Receiver));
            Assert.That(s.Receiver.ActiveTimeline, Is.SameAs(s.Timeline));
            Assert.That(s.Receiver.ActiveDirector, Is.SameAs(s.Host.Director));
            Assert.That(s.Receiver.LastBakeLocate.Status, Is.EqualTo(BakeLocateStatus.Found));
            Assert.That(s.Receiver.ConnectedLayerNames, Is.EqualTo(new[] { EmotionLayer }),
                "Profile に Target Layer Names を書かずに、トラック名で導出したレイヤーが接続される");
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.True);
            Assert.That(s.Receiver.TryGetExpressionSink(EmotionLayer, out TimelineExpressionStateSink stateSink), Is.True);
            Assert.That(stateSink, Is.Not.Null);
            Assert.That(s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);
            Assert.That(valueSink, Is.Not.Null);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LayerConnected, EmotionLayer), Is.True);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeFresh), Is.True);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True);
            Assert.That(s.Receiver.Diagnostics.HasErrors, Is.False);
        }

        [Test]
        public void BeginPlaybackSession_CalledRepeatedly_IsIdempotent()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();
            s.Begin();
            s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink first);
            int revision = s.Receiver.Diagnostics.Revision;

            s.Begin();
            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(s.Receiver.Diagnostics.Revision, Is.EqualTo(revision), "Active 中の Begin は何もしない");
            Assert.That(s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink again), Is.True);
            Assert.That(again, Is.SameAs(first));
        }

        [Test]
        public void SampleExpressionValues_ActiveSession_WritesBakeCurveIntoValueSink()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();
            s.Begin();
            Assert.That(s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);

            s.Receiver.SampleExpressionValues(EmotionLayer, 0.5d);

            AssertSinkValue(valueSink, 0, 0.6f);
        }

        // ================================================================
        // Pending（controller 未初期化）
        // ================================================================

        [Test]
        public void BeginPlaybackSession_ControllerNotInitialized_StaysPendingWithoutLogAndRetries()
        {
            Scenario s = CreateScenario(initializeController: false);
            s.Host.Attach();
            using var logs = new TimelineLogCounter();

            s.Begin();
            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Pending));
            Assert.That(logs.Errors + logs.Warnings, Is.EqualTo(0), "Pending はログを出さない");
            Assert.That(s.Receiver.ConnectedLayerNames, Is.Empty);

            s.Host.InitializeController();
            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), "初期化後の再試行で Active になる");
            Assert.That(s.Receiver.ConnectedLayerNames, Is.EqualTo(new[] { EmotionLayer }));
        }

        // ================================================================
        // Failed 条件と判定順
        // ================================================================

        [Test]
        public void BeginPlaybackSession_WithoutBinding_FailsWithBindingMissing()
        {
            Scenario s = CreateScenario();
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BindingMissing), Is.True);
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void BeginPlaybackSession_BindingDisabled_FailsWithBindingDisabled()
        {
            Scenario s = CreateScenario();
            s.Host.Attach(enabled: false);
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BindingDisabled), Is.True);
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False);
        }

        [Test]
        public void BeginPlaybackSession_ReceiverOnOtherGameObject_FailsWithReceiverNotOnControllerObject()
        {
            Scenario s = CreateScenario();
            var other = new GameObject("OtherReceiver");
            _disposables.Add(new DestroyOnDispose(other));
            var receiver = other.AddComponent<FacialTimelineReceiver>();
            receiver.AttachBinding(s.Host.Context());
            using var logs = new TimelineLogCounter();

            receiver.BeginPlaybackSession(s.Timeline, s.Host.Director);

            Assert.That(receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(receiver.Diagnostics.Contains(TimelineDiagnosticCode.ReceiverNotOnControllerObject), Is.True);
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void BeginPlaybackSession_BakeReferenceConflict_FailsAndDoesNotConnect()
        {
            Scenario s = CreateScenario();
            var second = s.Timeline.CreateTrack<FacialExpressionTrack>(null, "other");
            second.Bake = s.Host.CreateBake(s.Timeline, (EmotionLayer, "smile", 0.1f));
            s.Host.Attach();
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeReferenceConflict), Is.True);
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False);
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void BeginPlaybackSession_LegacyExportWithoutTrackReferences_Fails()
        {
            Scenario s = CreateScenario();
            s.Track.Bake = null;
            s.Host.Attach();
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeLegacyExport), Is.True);
        }

        [Test]
        public void BeginPlaybackSession_LegacyStateDeclaration_FailsAndLeavesRegistryUnchanged()
        {
            Scenario s = CreateScenario(declarations: new[] { new InputSourceDeclaration(StateId, 1f, null) });
            s.Host.Attach();
            string[] idsBefore = s.Host.SnapshotRegistryIds();
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.True);
            Assert.That(s.Host.SnapshotRegistryIds(), Is.EqualTo(idsBefore));
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False);
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void BeginPlaybackSession_MultipleFailureCauses_ReportsBindingFirstAndLogsOnce()
        {
            // (1) binding 無効 + (2) Bake 参照不整合 + (3) 旧 :state 宣言 が同時にある。
            Scenario s = CreateScenario(declarations: new[] { new InputSourceDeclaration(StateId, 1f, null) });
            s.Track.Bake = null;
            s.Host.Attach(enabled: false);
            using var logs = new TimelineLogCounter();

            s.Begin();
            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BindingDisabled), Is.True);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeLegacyExport), Is.False);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.False);
            Assert.That(logs.Errors, Is.EqualTo(1), "Console には最初の Error 1 件のみ");
        }

        [Test]
        public void BeginPlaybackSession_BakeConflictAndLegacyDeclaration_ReportsBakeBeforeDeclaration()
        {
            Scenario s = CreateScenario(declarations: new[] { new InputSourceDeclaration(StateId, 1f, null) });
            s.Track.Bake = null;
            s.Host.Attach();
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Failed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeLegacyExport), Is.True);
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.False);
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void BeginPlaybackSession_OtherDirectorWhileActive_RecordsSessionConflictAndKeepsSession()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();
            s.Begin();
            var otherObject = new GameObject("OtherDirector");
            _disposables.Add(new DestroyOnDispose(otherObject));
            var otherDirector = otherObject.AddComponent<PlayableDirector>();
            using var logs = new TimelineLogCounter();

            s.Receiver.BeginPlaybackSession(s.Timeline, otherDirector);

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(s.Receiver.ActiveDirector, Is.SameAs(s.Host.Director));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.SessionConflict), Is.True);
            Assert.That(s.Receiver.IsSessionOwnedBy(otherDirector), Is.False);
            Assert.That(s.Receiver.IsSessionOwnedBy(s.Host.Director), Is.True);
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        // ================================================================
        // Warning で Active を維持
        // ================================================================

        [Test]
        public void BeginPlaybackSession_ProfileMismatch_StaysActiveAndPlaysBakeValues()
        {
            Scenario s = CreateScenario();
            s.Bake.ProfileContentHashHex = "deadbeefdeadbeef";
            s.Host.Attach();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMismatch), Is.True);
            Assert.That(SeverityOf(s.Receiver.Diagnostics, TimelineDiagnosticCode.ProfileMismatch),
                Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);
            s.Receiver.SampleExpressionValues(EmotionLayer, 0.5d);
            AssertSinkValue(valueSink, 0, 0.6f);
        }

        [Test]
        public void BeginPlaybackSession_BakeStale_StaysActiveWithWarning()
        {
            Scenario s = CreateScenario();
            s.Bake.SourceHashHex = "0000000000000000";
            s.Host.Attach();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeStale), Is.True);
            Assert.That(SeverityOf(s.Receiver.Diagnostics, TimelineDiagnosticCode.BakeStale),
                Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True);
        }

        [Test]
        public void BeginPlaybackSession_OverrideBake_UsesOverrideForValues()
        {
            Scenario s = CreateScenario();
            FacialTimelineBakeAsset overrideBake = s.Host.CreateBake(s.Timeline, (EmotionLayer, "smile", 0.25f));
            s.Receiver.BakeAsset = overrideBake;
            s.Host.Attach();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(s.Receiver.LastBakeLocate.Status, Is.EqualTo(BakeLocateStatus.OverrideUsed));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeOverrideDiffers), Is.True);
            s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink valueSink);
            s.Receiver.SampleExpressionValues(EmotionLayer, 0.5d);
            AssertSinkValue(valueSink, 0, 0.25f);
        }

        // ================================================================
        // Analog 乗っ取り（Takeover への委譲）
        // ================================================================

        [Test]
        public void BeginPlaybackSession_AnalogChannel_TakesOverRegistryEntryAndReleaseRestores()
        {
            Scenario s = CreateScenario(withAnalogChannel: true);
            var original = new TestAnalogSource("osc:lt", 1, 0.2f);
            s.Host.Registry.Register(AdapterSlug.Parse("osc"), "lt", original);
            s.Host.Attach();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), Describe(s.Receiver));
            Assert.That(s.Receiver.TryGetAnalogSink("osc:lt", out TimelineAnalogInputSource sink), Is.True);
            Assert.That(s.Host.Registry.TryResolve("osc:lt", out IInputSource during), Is.True);
            Assert.That(during, Is.SameAs(sink));
            Assert.That(s.Receiver.TakeoverEntries, Has.Count.EqualTo(1));
            Assert.That(s.Receiver.TakeoverEntries[0].IsAttached, Is.True);

            s.Receiver.ReleaseAll();

            Assert.That(s.Host.Registry.TryResolve("osc:lt", out IInputSource after), Is.True);
            Assert.That(after, Is.SameAs(original));
            Assert.That(s.Receiver.TryGetAnalogSink("osc:lt", out _), Is.False);
            Assert.That(s.Receiver.TakeoverEntries, Is.Empty);
        }

        // ================================================================
        // ReleaseAll
        // ================================================================

        [Test]
        public void ReleaseAll_AfterActive_RestoresRegistryAndLayerConfigurationAndReturnsToIdle()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();
            string[] idsBefore = s.Host.SnapshotRegistryIds();
            s.Begin();
            s.Receiver.TryGetExpressionSink(EmotionLayer, out TimelineExpressionStateSink stateSink);
            s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink valueSink);
            stateSink.TriggerOn("smile");
            s.Receiver.SampleExpressionValues(EmotionLayer, 0.5d);

            s.Receiver.ReleaseAll();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(s.Receiver.ActiveTimeline, Is.Null);
            Assert.That(s.Host.SnapshotRegistryIds(), Is.EqualTo(idsBefore), "registry が接続前に戻る");
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False, "レイヤー構成が戻る");
            Assert.That(s.Receiver.ConnectedLayerNames, Is.Empty);
            Assert.That(stateSink.ActiveExpressionIds, Is.Empty);
            Assert.That(valueSink.IsValid, Is.False);
            Assert.That(s.Receiver.TryGetExpressionSink(EmotionLayer, out _), Is.False);
            Assert.DoesNotThrow(() => s.Receiver.ReleaseAll(), "二重解放は no-op");
        }

        [Test]
        public void BeginPlaybackSession_AfterReleaseWithSameInputs_ReusesPooledSessionResources()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();
            s.Begin();
            s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink first);
            s.Receiver.ReleaseAll();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active));
            Assert.That(s.Receiver.TryGetExpressionValueSink(EmotionLayer, out TimelineBakedValueSink second), Is.True);
            Assert.That(second, Is.SameAs(first), "(Timeline, Bake, Profile) が同じ間はセッション資源を再利用する");
            s.Receiver.SampleExpressionValues(EmotionLayer, 0.5d);
            AssertSinkValue(second, 0, 0.6f);
        }

        [Test]
        public void DetachBinding_AfterActive_ReleasesAndRequiresNewBinding()
        {
            Scenario s = CreateScenario();
            s.Host.Attach();
            s.Begin();

            s.Receiver.DetachBinding();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(s.Receiver.IsBindingAttached, Is.False);
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False);
        }

        [Test]
        public void BeginPlaybackSession_WithoutBindingAndControllerNotInitialized_StaysPendingWithoutLog()
        {
            // 6.1 レビュー F3: binding の OnStart は controller の初期化中に呼ばれるため、未初期化の間は binding 未接続を確定しない。
            Scenario s = CreateScenario(initializeController: false);
            using var logs = new TimelineLogCounter();

            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Pending), Describe(s.Receiver));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BindingMissing), Is.False);
            Assert.That(logs.Errors + logs.Warnings, Is.EqualTo(0));

            s.Host.InitializeController();
            s.Host.Attach();
            s.Begin();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), Describe(s.Receiver));
        }

        // ================================================================
        // ライフサイクル（Play の OnEnable / Start）
        // ================================================================

        [Test]
        public void OnEnableInPlay_FacialTracksWithoutBinding_BindsTracksToReceiver()
        {
            Scenario s = CreateScenario();
            s.Host.Director.ClearGenericBinding(s.Track);
            Assert.That(s.Host.Director.GetGenericBinding(s.Track), Is.Null, "fixture: binding 未設定");

            s.Receiver.OnEnableInPlay();

            Assert.That(s.Host.Director.GetGenericBinding(s.Track), Is.SameAs(s.Receiver),
                "Receiver を置くだけで Facial トラックの binding が自分を指す");
        }

        [Test]
        public void OnEnableInPlay_TrackBoundToOtherObject_LeavesBindingAndReportsForeignOnStart()
        {
            Scenario s = CreateScenario(withProfileSource: true);
            var other = new GameObject("ForeignBindingTarget");
            _disposables.Add(new DestroyOnDispose(other));
            s.Host.Director.SetGenericBinding(s.Track, other);
            using var logs = new TimelineLogCounter();

            s.Receiver.OnEnableInPlay();
            s.Receiver.StartInPlay();

            Assert.That(s.Host.Director.GetGenericBinding(s.Track), Is.SameAs(other), "他者の binding は上書きしない");
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.TrackBindingForeign, EmotionLayer), Is.True,
                Describe(s.Receiver));
            Assert.That(logs.Warnings, Is.EqualTo(1));
        }

        [Test]
        public void StartInPlay_FullyConfigured_EvaluatesStaticDiagnosticsWithoutConsoleOutput()
        {
            Scenario s = CreateScenario(withProfileSource: true);
            using var logs = new TimelineLogCounter();

            s.Receiver.OnEnableInPlay();
            s.Receiver.StartInPlay();

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeFresh), Is.True, Describe(s.Receiver));
            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.ProfileMatched), Is.True);
            Assert.That(s.Receiver.Diagnostics.HasErrors, Is.False, Describe(s.Receiver));
            Assert.That(logs.Errors + logs.Warnings, Is.EqualTo(0), "Info は Console に出さない");
        }

        [Test]
        public void StartInPlay_ReceiverOnChildOfControllerObject_ReportsReceiverNotOnControllerObject()
        {
            Scenario s = CreateScenario(withProfileSource: true);
            var child = new GameObject("ChildReceiver");
            _disposables.Add(new DestroyOnDispose(child));
            child.transform.SetParent(s.Host.Root.transform, false);
            var receiver = child.AddComponent<FacialTimelineReceiver>();
            using var logs = new TimelineLogCounter();

            receiver.OnEnableInPlay();
            receiver.StartInPlay();

            Assert.That(receiver.Diagnostics.Contains(TimelineDiagnosticCode.ReceiverNotOnControllerObject), Is.True,
                Describe(receiver));
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void StartInPlay_NoDirector_ReportsDirectorMissing()
        {
            Scenario s = CreateScenario(withProfileSource: true);
            UnityEngine.Object.DestroyImmediate(s.Host.Director);
            using var logs = new TimelineLogCounter();

            s.Receiver.OnEnableInPlay();
            s.Receiver.StartInPlay();

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.DirectorMissing), Is.True, Describe(s.Receiver));
            Assert.That(logs.Errors, Is.EqualTo(1));
        }

        [Test]
        public void StartInPlay_SameWarningWithinSession_IsLoggedOnceAndAgainAfterReleaseAll()
        {
            Scenario s = CreateScenario(withProfileSource: true);
            s.Bake.SourceHashHex = "0000000000000000";
            s.Host.Attach();
            using var logs = new TimelineLogCounter();

            s.Receiver.StartInPlay();
            s.Receiver.StartInPlay();
            s.Begin();

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.BakeStale), Is.True, Describe(s.Receiver));
            Assert.That(logs.Warnings, Is.EqualTo(1), "同一セッションで同じ警告は 1 回だけ");

            s.Receiver.ReleaseAll();
            s.Receiver.StartInPlay();

            Assert.That(logs.Warnings, Is.EqualTo(2), "ReleaseAll で警告ゲートのエポックがリセットされる");
        }

        [Test]
        public void EvaluateStaticDiagnostics_WithExplicitProfile_UsesGivenProfileForLayerMatch()
        {
            Scenario s = CreateScenario(withProfileSource: true);
            var otherProfile = new FacialProfile(
                "1.0",
                new[] { new LayerDefinition("eye", 0, ExclusionMode.LastWins) },
                Array.Empty<Expression>());

            s.Receiver.EvaluateStaticDiagnostics(otherProfile, hasProfile: true);

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched, EmotionLayer), Is.True,
                Describe(s.Receiver));

            s.Receiver.EvaluateStaticDiagnostics();

            Assert.That(s.Receiver.Diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched), Is.False,
                "引数なしは controller の Profile で評価する");
        }

        [Test]
        public void OnDisableInPlay_AfterActiveWithTakeover_ReleasesConnectionsAndTakeovers()
        {
            Scenario s = CreateScenario(withAnalogChannel: true);
            var original = new TestAnalogSource("osc:lt", 1, 0.2f);
            s.Host.Registry.Register(AdapterSlug.Parse("osc"), "lt", original);
            s.Host.Attach();
            s.Begin();
            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Active), Describe(s.Receiver));

            s.Receiver.OnDisableInPlay();

            Assert.That(s.Receiver.SessionState, Is.EqualTo(TimelineSessionState.Idle));
            Assert.That(s.Host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False);
            Assert.That(s.Host.Registry.TryResolve("osc:lt", out IInputSource after), Is.True);
            Assert.That(after, Is.SameAs(original));
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private sealed class Scenario
        {
            public TimelineReceiverTestHost Host;
            public TimelineAsset Timeline;
            public FacialExpressionTrack Track;
            public FacialTimelineBakeAsset Bake;

            public FacialTimelineReceiver Receiver => Host.Receiver;

            public void Begin()
            {
                Receiver.BeginPlaybackSession(Timeline, Host.Director);
            }
        }

        private Scenario CreateScenario(
            bool initializeController = true,
            InputSourceDeclaration[] declarations = null,
            bool withAnalogChannel = false,
            bool withProfileSource = false)
        {
            FacialProfile profile = CreateProfile(declarations);
            TimelineReceiverTestHost host = TimelineReceiverTestHost.Create(profile, BlendShapes, initializeController);
            _disposables.Add(host);
            if (withProfileSource)
            {
                host.AssignProfileSource();
            }

            TimelineAsset timeline = host.CreateTimeline();
            FacialExpressionTrack track = timeline.CreateTrack<FacialExpressionTrack>(null, EmotionLayer);
            TimelineClip clip = track.CreateClip<FacialExpressionClip>();
            clip.start = 0d;
            clip.duration = 1d;
            ((FacialExpressionClip)clip.asset).ExpressionId = "smile";

            if (withAnalogChannel)
            {
                FacialValueTrack valueTrack = timeline.CreateTrack<FacialValueTrack>(null, "lt");
                valueTrack.ChannelSubId = "osc:lt";
                valueTrack.ChannelKind = FacialValueChannelKind.Analog;
                TimelineClip valueClip = valueTrack.CreateClip<FacialValueClip>();
                valueClip.start = 0d;
                valueClip.duration = 1d;
                ((FacialValueClip)valueClip.asset).Axes = new[] { AnimationCurve.Constant(0f, 1f, 0.7f) };
            }

            FacialTimelineBakeAsset bake = host.CreateBake(timeline, (EmotionLayer, "smile", 0.6f));
            TimelineReceiverTestHost.AssignBakeToAllTracks(timeline, bake);
            host.StampHashes(timeline, bake);
            host.BindDirector(timeline);

            return new Scenario { Host = host, Timeline = timeline, Track = track, Bake = bake };
        }

        private static FacialProfile CreateProfile(InputSourceDeclaration[] declarations)
        {
            var layers = new[] { new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins) };
            var expressions = new[]
            {
                new Expression(
                    "smile", "Smile", EmotionLayer, 0.05f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("smile", 1f) }),
            };
            InputSourceDeclaration[][] layerInputSources = declarations == null ? null : new[] { declarations };
            return new FacialProfile("1.0", layers, expressions, layerInputSources: layerInputSources);
        }

        private static void AssertSinkValue(TimelineBakedValueSink sink, int index, float expected)
        {
            Span<float> output = stackalloc float[BlendShapes.Length];
            output.Clear();
            Assert.That(sink.TryWriteValues(output), Is.True, "値 sink が有効であること");
            Assert.That(output[index], Is.EqualTo(expected).Within(1e-5f));
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

            Assert.Fail($"診断 {code} が記録されていない");
            return default;
        }

        private static string Describe(FacialTimelineReceiver receiver)
        {
            var parts = new List<string>();
            foreach (TimelineDiagnosticItem item in receiver.Diagnostics.Items)
            {
                parts.Add($"{item.Area}/{item.Code}/{item.Severity}/{item.Subject}");
            }

            return $"state={receiver.SessionState} items=[" + string.Join(", ", parts) + "]";
        }

        private sealed class DestroyOnDispose : IDisposable
        {
            private readonly UnityEngine.Object _target;

            public DestroyOnDispose(UnityEngine.Object target)
            {
                _target = target;
            }

            public void Dispose()
            {
                if (_target != null)
                {
                    UnityEngine.Object.DestroyImmediate(_target);
                }
            }
        }
    }
}
