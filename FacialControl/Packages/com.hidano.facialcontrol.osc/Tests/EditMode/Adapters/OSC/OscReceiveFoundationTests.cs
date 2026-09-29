using System;
using NUnit.Framework;
using Hidano.FacialControl.Adapters.OSC;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscReceiveFoundationTests : SizedTestFixture
    {
        [Test]
        public void ReceiveOptions_UsesDefaultsAndRejectsValuesOutsideContract()
        {
            var options = OscReceiveOptions.Default;

            Assert.That(options.DatagramSlotBytes, Is.EqualTo(2048));
            Assert.That(options.DatagramSlotCount, Is.EqualTo(32));
            Assert.That(options.SocketReceiveBufferBytes, Is.EqualTo(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OscReceiveOptions(511, 32, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OscReceiveOptions(2048, 3, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new OscReceiveOptions(2048, 32, 8191));
        }

        [Test]
        public void Diagnostics_ExposesCountersAndDetectsZeroToNonZeroOnce()
        {
            var diagnostics = new OscReceiveDiagnostics();

            diagnostics.IncrementReceivedDatagrams();
            diagnostics.IncrementReceivedDatagrams();
            diagnostics.IncrementDroppedDatagrams();

            Assert.That(diagnostics.ReceivedDatagramCount, Is.EqualTo(2));
            Assert.That(diagnostics.DroppedDatagramCount, Is.EqualTo(1));
            Assert.That(diagnostics.TryMarkWarning(OscDiagnosticWarning.DroppedDatagram), Is.True);
            Assert.That(diagnostics.TryMarkWarning(OscDiagnosticWarning.DroppedDatagram), Is.False);
            Assert.That(diagnostics.TryMarkWarning(OscDiagnosticWarning.OversizedDatagram), Is.True);
        }

        [Test]
        public void ControlAddresses_ExposeUtf8Values()
        {
            Assert.That(OscControlAddresses.SenderId, Is.EqualTo("/_facialcontrol/sender_id"));
            Assert.That(OscControlAddresses.SenderIdUtf8, Is.EqualTo(System.Text.Encoding.UTF8.GetBytes(OscControlAddresses.SenderId)));
            Assert.That(OscTypeTag.HasPayload(OscTypeTag.Float32), Is.True);
            Assert.That(OscTypeTag.HasPayload(OscTypeTag.True), Is.False);
            Assert.That(OscTypeTag.IsKnown(OscTypeTag.False), Is.True);
            Assert.That(OscTypeTag.IsKnown((byte)'x'), Is.False);
        }
    }
}
