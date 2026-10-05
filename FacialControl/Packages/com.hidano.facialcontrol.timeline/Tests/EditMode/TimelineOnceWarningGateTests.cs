using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineOnceWarningGateTests : SizedTestFixture
    {
        [Test]
        public void TryPass_FirstCall_ReturnsTrueAndSecondCallReturnsFalse()
        {
            var gate = new TimelineOnceWarningGate();

            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.AnalogSourceNotFound, "osc:lt"), Is.True);
            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.AnalogSourceNotFound, "osc:lt"), Is.False);
            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.AnalogSourceNotFound, "osc:lt"), Is.False);
        }

        [Test]
        public void TryPass_DifferentSubject_IsSeparateKey()
        {
            var gate = new TimelineOnceWarningGate();
            gate.TryPass(100, TimelineDiagnosticCode.TrackLayerUnmatched, "mouth");

            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.TrackLayerUnmatched, "eye"), Is.True);
        }

        [Test]
        public void TryPass_DifferentOwner_IsSeparateKey()
        {
            var gate = new TimelineOnceWarningGate();
            gate.TryPass(100, TimelineDiagnosticCode.TrackLayerUnmatched, "mouth");

            Assert.That(gate.TryPass(200, TimelineDiagnosticCode.TrackLayerUnmatched, "mouth"), Is.True);
        }

        [Test]
        public void TryPass_DifferentCode_IsSeparateKey()
        {
            var gate = new TimelineOnceWarningGate();
            gate.TryPass(100, TimelineDiagnosticCode.AnalogSourceNotFound, "osc:lt");

            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.AnalogOccupied, "osc:lt"), Is.True);
        }

        [Test]
        public void TryPass_NullAndEmptySubject_AreTheSameKey()
        {
            var gate = new TimelineOnceWarningGate();
            gate.TryPass(100, TimelineDiagnosticCode.DirectorMissing, null);

            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.DirectorMissing, string.Empty), Is.False);
        }

        [Test]
        public void ResetEpoch_AllowsTheSameKeyAgain()
        {
            var gate = new TimelineOnceWarningGate();
            gate.TryPass(100, TimelineDiagnosticCode.ProfileMismatch, "Character");
            gate.TryPass(200, TimelineDiagnosticCode.BakeStale, "Timeline");

            gate.ResetEpoch();

            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.ProfileMismatch, "Character"), Is.True);
            Assert.That(gate.TryPass(100, TimelineDiagnosticCode.ProfileMismatch, "Character"), Is.False);
            Assert.That(gate.TryPass(200, TimelineDiagnosticCode.BakeStale, "Timeline"), Is.True);
        }
    }
}
