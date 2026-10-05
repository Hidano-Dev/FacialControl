using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Editor;
using Hidano.FacialControl.Timeline.Editor.Inspector;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Receiver Inspector の表示判定（どの診断で何を表示し、どのボタンを有効にするか）を UI の外で固定する。
    /// </summary>
    [SmallTest]
    public sealed class FacialTimelineReceiverInspectorModelTests : SizedTestFixture
    {
        [Test]
        public void GroupByArea_OrdersByAreaAndMarksHealthyAreas()
        {
            var items = new[]
            {
                Item(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeFresh, TimelineDiagnosticSeverity.Info),
                Item(TimelineDiagnosticArea.Profile, TimelineDiagnosticCode.ProfileMismatch, TimelineDiagnosticSeverity.Warning),
                Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorMissing, TimelineDiagnosticSeverity.Error),
            };

            IReadOnlyList<DiagnosticAreaGroup> groups = FacialTimelineReceiverInspectorModel.GroupByArea(items);

            Assert.That(groups.Count, Is.EqualTo(3));
            Assert.That(groups[0].Area, Is.EqualTo(TimelineDiagnosticArea.Director));
            Assert.That(groups[0].Worst, Is.EqualTo(TimelineDiagnosticSeverity.Error));
            Assert.That(groups[0].IsHealthy, Is.False);
            Assert.That(groups[1].Area, Is.EqualTo(TimelineDiagnosticArea.Bake));
            Assert.That(groups[1].IsHealthy, Is.True, "Info / Ok のみの領域は 1 行に畳む");
            Assert.That(groups[2].Area, Is.EqualTo(TimelineDiagnosticArea.Profile));
            Assert.That(groups[2].Worst, Is.EqualTo(TimelineDiagnosticSeverity.Warning));
        }

        [Test]
        public void GroupByArea_Empty_ReturnsEmpty()
        {
            Assert.That(FacialTimelineReceiverInspectorModel.GroupByArea(new TimelineDiagnosticItem[0]), Is.Empty);
        }

        [TestCase(TimelineDiagnosticSeverity.Error, "console.erroricon.sml")]
        [TestCase(TimelineDiagnosticSeverity.Warning, "console.warnicon.sml")]
        [TestCase(TimelineDiagnosticSeverity.Info, "console.infoicon.sml")]
        [TestCase(TimelineDiagnosticSeverity.Ok, "TestPassed")]
        public void SeverityIconName_MapsEachSeverity(TimelineDiagnosticSeverity severity, string expected)
        {
            Assert.That(FacialTimelineReceiverInspectorModel.SeverityIconName(severity), Is.EqualTo(expected));
        }

        [Test]
        public void AreaLabel_AllAreasHaveLabel()
        {
            foreach (TimelineDiagnosticArea area in System.Enum.GetValues(typeof(TimelineDiagnosticArea)))
            {
                Assert.That(FacialTimelineReceiverInspectorModel.AreaLabel(area), Is.Not.Empty, area.ToString());
            }
        }

        [TestCase(TimelineDiagnosticCode.BakeReferenceConflict, true, true)]
        [TestCase(TimelineDiagnosticCode.BakeLegacyExport, true, true)]
        [TestCase(TimelineDiagnosticCode.ProfileMismatch, true, true)]
        [TestCase(TimelineDiagnosticCode.ProfileMismatch, false, false)]
        [TestCase(TimelineDiagnosticCode.LegacyStateDeclaration, true, false)]
        [TestCase(TimelineDiagnosticCode.BakeStale, true, false)]
        public void ShowsAutoRebakeNote_OnlyForAutoRepairableCodesWhilePending(TimelineDiagnosticCode code, bool pending, bool expected)
        {
            Assert.That(FacialTimelineReceiverInspectorModel.ShowsAutoRebakeNote(code, pending), Is.EqualTo(expected));
        }

        [Test]
        public void CanRemoveLegacyDeclarations_OnlyInEditWithLegacyStateDeclaration()
        {
            var withLegacy = new FacialTimelineDiagnostics();
            withLegacy.ReplaceArea(TimelineDiagnosticArea.LayerConnection, new[]
            {
                Item(TimelineDiagnosticArea.LayerConnection, TimelineDiagnosticCode.LegacyStateDeclaration, TimelineDiagnosticSeverity.Error),
            });
            var clean = new FacialTimelineDiagnostics();

            Assert.That(FacialTimelineReceiverInspectorModel.CanRemoveLegacyDeclarations(isPlaying: false, withLegacy), Is.True);
            Assert.That(FacialTimelineReceiverInspectorModel.CanRemoveLegacyDeclarations(isPlaying: true, withLegacy), Is.False);
            Assert.That(FacialTimelineReceiverInspectorModel.CanRemoveLegacyDeclarations(isPlaying: false, clean), Is.False);
        }

        [Test]
        public void ShowsOverrideDiffers_WhenDiagnosticPresent()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            Assert.That(FacialTimelineReceiverInspectorModel.ShowsOverrideDiffers(diagnostics), Is.False);

            diagnostics.ReplaceArea(TimelineDiagnosticArea.Bake, new[]
            {
                Item(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeOverrideDiffers, TimelineDiagnosticSeverity.Warning),
            });

            Assert.That(FacialTimelineReceiverInspectorModel.ShowsOverrideDiffers(diagnostics), Is.True);
        }

        [Test]
        public void ButtonStates_FollowEvaluation()
        {
            var ready = new ReceiverEditEvaluation(
                director: null, timeline: null, profileAsset: null,
                hasDirector: true, hasTimeline: true, timelineSaved: true,
                unboundTrackCount: 2, hasLegacyStateDeclarations: false, autoRebakePending: false);
            var unsaved = new ReceiverEditEvaluation(
                director: null, timeline: null, profileAsset: null,
                hasDirector: true, hasTimeline: true, timelineSaved: false,
                unboundTrackCount: 0, hasLegacyStateDeclarations: false, autoRebakePending: false);

            Assert.That(FacialTimelineReceiverInspectorModel.CanAssignTrackBindings(false, ready), Is.True);
            Assert.That(FacialTimelineReceiverInspectorModel.CanAssignTrackBindings(true, ready), Is.False, "Play 中は Edit の操作をしない");
            Assert.That(FacialTimelineReceiverInspectorModel.CanAssignTrackBindings(false, unsaved), Is.False, "未設定トラックが無い");
            Assert.That(FacialTimelineReceiverInspectorModel.CanRebake(false, ready), Is.True);
            Assert.That(FacialTimelineReceiverInspectorModel.CanRebake(false, unsaved), Is.False, "未保存 Timeline は再ベイクできない");
            Assert.That(FacialTimelineReceiverInspectorModel.CanRebake(true, ready), Is.False);
        }

        [TestCase(TimelineSessionState.Idle)]
        [TestCase(TimelineSessionState.Pending)]
        [TestCase(TimelineSessionState.Active)]
        [TestCase(TimelineSessionState.Failed)]
        public void SessionStateLabel_AllStatesHaveLabel(TimelineSessionState state)
        {
            Assert.That(FacialTimelineReceiverInspectorModel.SessionStateLabel(state), Is.Not.Empty);
        }

        private static TimelineDiagnosticItem Item(TimelineDiagnosticArea area, TimelineDiagnosticCode code, TimelineDiagnosticSeverity severity)
        {
            return new TimelineDiagnosticItem(area, code, severity, "subject", "detail");
        }
    }
}
