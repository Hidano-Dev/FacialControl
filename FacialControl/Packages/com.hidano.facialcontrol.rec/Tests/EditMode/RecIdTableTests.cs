using System;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecIdTableTests : SizedTestFixture
    {
        [Test]
        public void AddDefinedId_SameSourceIdAtDifferentIndex_ThrowsInvalidOperationException()
        {
            var table = new RecIdTable();
            table.AddDefinedId(0, RecEvent.IdDefinitionKind.Source, "osc");

            Assert.Throws<InvalidOperationException>(() =>
                table.AddDefinedId(1, RecEvent.IdDefinitionKind.Source, "osc"));
        }

        [Test]
        public void CreateSeeded_EmptyBaseline_RegistersReservedExpressionSource()
        {
            RecIdTable table = RecIdTable.CreateSeeded(RecBaselineState.Empty);

            Assert.That(table.SourceIds, Is.EqualTo(new[] { "@expression" }));
            Assert.That(table.ExpressionIds, Is.Empty);
        }

        [Test]
        public void CreateSeeded_FullBaseline_OrdersTriggerAnalogValueProviderThenReservedSource()
        {
            var baseline = new RecBaselineState(
                new[] { new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }) },
                new[] { new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.5f }) },
                new[] { new RecBaselineState.ValueProviderEntry("input:osc", true, new byte[] { 1 }, new[] { 0.1f }) },
                new[] { "angry" });

            RecIdTable table = RecIdTable.CreateSeeded(baseline);

            Assert.That(table.SourceIds, Is.EqualTo(new[] { "input:trigger", "input:gaze", "input:osc", "@expression" }));
            Assert.That(table.ExpressionIds, Is.EqualTo(new[] { "smile", "angry" }));
        }
    }
}
