using System;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class FacialTimelineDiagnosticsTests : SizedTestFixture
    {
        [Test]
        public void NewInstance_IsEmptyWithOkOverall()
        {
            var diagnostics = new FacialTimelineDiagnostics();

            Assert.That(diagnostics.Items, Is.Empty);
            Assert.That(diagnostics.Revision, Is.EqualTo(0));
            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Ok));
            Assert.That(diagnostics.HasErrors, Is.False);
        }

        [Test]
        public void ReplaceArea_IncrementsRevisionAndRaisesChanged()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            int changedCount = 0;
            diagnostics.Changed += () => changedCount++;

            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorMissing, TimelineDiagnosticSeverity.Error) });

            Assert.That(diagnostics.Revision, Is.EqualTo(1));
            Assert.That(changedCount, Is.EqualTo(1));
            Assert.That(diagnostics.Items.Count, Is.EqualTo(1));
            Assert.That(diagnostics.Items[0].Code, Is.EqualTo(TimelineDiagnosticCode.DirectorMissing));
        }

        [Test]
        public void ReplaceArea_ReplacesOnlyItemsOfTheSameArea()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorMissing, TimelineDiagnosticSeverity.Error) });
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Bake,
                new[] { Item(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeStale, TimelineDiagnosticSeverity.Warning) });

            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.Ok, TimelineDiagnosticSeverity.Ok) });

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.DirectorMissing), Is.False);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.Ok), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.BakeStale), Is.True);
            Assert.That(diagnostics.Items.Count, Is.EqualTo(2));
            Assert.That(diagnostics.Revision, Is.EqualTo(3));
        }

        [Test]
        public void ReplaceArea_EmptyItems_RemovesTheArea()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Analog,
                new[] { Item(TimelineDiagnosticArea.Analog, TimelineDiagnosticCode.AnalogSourceNotFound, TimelineDiagnosticSeverity.Warning, "osc:lt") });

            diagnostics.ReplaceArea(TimelineDiagnosticArea.Analog, ReadOnlySpan<TimelineDiagnosticItem>.Empty);

            Assert.That(diagnostics.Items, Is.Empty);
            Assert.That(diagnostics.Revision, Is.EqualTo(2));
        }

        [Test]
        public void ReplaceArea_ItemsAreOrderedByArea()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Session,
                new[] { Item(TimelineDiagnosticArea.Session, TimelineDiagnosticCode.SessionConflict, TimelineDiagnosticSeverity.Error) });
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorMissing, TimelineDiagnosticSeverity.Error) });

            Assert.That(diagnostics.Items[0].Area, Is.EqualTo(TimelineDiagnosticArea.Director));
            Assert.That(diagnostics.Items[1].Area, Is.EqualTo(TimelineDiagnosticArea.Session));
        }

        [Test]
        public void ReplaceArea_ItemOfDifferentArea_Throws()
        {
            var diagnostics = new FacialTimelineDiagnostics();

            Assert.Throws<ArgumentException>(() => diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeStale, TimelineDiagnosticSeverity.Warning) }));
            Assert.That(diagnostics.Revision, Is.EqualTo(0));
        }

        [Test]
        public void Overall_ReturnsMaximumSeverity()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Bake,
                new[] { Item(TimelineDiagnosticArea.Bake, TimelineDiagnosticCode.BakeStale, TimelineDiagnosticSeverity.Warning) });
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.LayerMatch,
                new[] { Item(TimelineDiagnosticArea.LayerMatch, TimelineDiagnosticCode.LayerSinkIdFallback, TimelineDiagnosticSeverity.Info, "感情") });

            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(diagnostics.HasErrors, Is.False);

            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorAmbiguous, TimelineDiagnosticSeverity.Error) });

            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Error));
            Assert.That(diagnostics.HasErrors, Is.True);
        }

        [Test]
        public void Overall_InfoOnly_ReturnsOk()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.TrackBinding,
                new[] { Item(TimelineDiagnosticArea.TrackBinding, TimelineDiagnosticCode.TrackBindingAutoAssigned, TimelineDiagnosticSeverity.Info) });

            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Ok));
        }

        [Test]
        public void Contains_WithSubject_MatchesCodeAndSubject()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.LayerMatch,
                new[] { Item(TimelineDiagnosticArea.LayerMatch, TimelineDiagnosticCode.TrackLayerUnmatched, TimelineDiagnosticSeverity.Warning, "mouth") });

            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched, "mouth"), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.TrackLayerUnmatched, "eye"), Is.False);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnected), Is.False);
        }

        [Test]
        public void Clear_RemovesAllItemsIncrementsRevisionAndRaisesChanged()
        {
            var diagnostics = new FacialTimelineDiagnostics();
            diagnostics.ReplaceArea(
                TimelineDiagnosticArea.Director,
                new[] { Item(TimelineDiagnosticArea.Director, TimelineDiagnosticCode.DirectorMissing, TimelineDiagnosticSeverity.Error) });
            int changedCount = 0;
            diagnostics.Changed += () => changedCount++;

            diagnostics.Clear();

            Assert.That(diagnostics.Items, Is.Empty);
            Assert.That(diagnostics.Revision, Is.EqualTo(2));
            Assert.That(changedCount, Is.EqualTo(1));
            Assert.That(diagnostics.Overall, Is.EqualTo(TimelineDiagnosticSeverity.Ok));
            Assert.That(diagnostics.HasErrors, Is.False);
        }

        [Test]
        public void Item_ExposesConstructorValuesAndNormalizesNullText()
        {
            var item = new TimelineDiagnosticItem(
                TimelineDiagnosticArea.Gaze,
                TimelineDiagnosticCode.GazeOccupied,
                TimelineDiagnosticSeverity.Warning,
                subject: null,
                detail: null);

            Assert.That(item.Area, Is.EqualTo(TimelineDiagnosticArea.Gaze));
            Assert.That(item.Code, Is.EqualTo(TimelineDiagnosticCode.GazeOccupied));
            Assert.That(item.Severity, Is.EqualTo(TimelineDiagnosticSeverity.Warning));
            Assert.That(item.Subject, Is.EqualTo(string.Empty));
            Assert.That(item.Detail, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SeverityOrder_InfoOkWarningError()
        {
            Assert.That((int)TimelineDiagnosticSeverity.Info, Is.LessThan((int)TimelineDiagnosticSeverity.Ok));
            Assert.That((int)TimelineDiagnosticSeverity.Ok, Is.LessThan((int)TimelineDiagnosticSeverity.Warning));
            Assert.That((int)TimelineDiagnosticSeverity.Warning, Is.LessThan((int)TimelineDiagnosticSeverity.Error));
        }

        private static TimelineDiagnosticItem Item(
            TimelineDiagnosticArea area,
            TimelineDiagnosticCode code,
            TimelineDiagnosticSeverity severity,
            string subject = "")
        {
            return new TimelineDiagnosticItem(area, code, severity, subject, "detail");
        }
    }
}
