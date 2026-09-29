using System;
using System.Linq;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using System.Text;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Adapters.OSC;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscDatagramRingTests : SizedTestFixture
    {
        private static OscReceiveOptions Options => new OscReceiveOptions(512, 4, 0);

        [Test]
        public void FullRing_DropsOldestCommittedSlot()
        {
            var diagnostics = new OscReceiveDiagnostics();
            var ring = new OscDatagramRing(Options, diagnostics);
            var drain = new OscDrainBuffer(Options);

            for (byte value = 1; value <= 4; value++)
            {
                Assert.That(ring.TryReserveSlot(out int slot), Is.True);
                ring.GetSlotBytes(slot)[0] = value;
                ring.Commit(slot, 1, 0, 1);
            }

            Assert.That(ring.TryReserveSlot(out int newest), Is.True);
            ring.GetSlotBytes(newest)[0] = 5;
            ring.Commit(newest, 1, 0, 1);

            Assert.That(diagnostics.DroppedDatagramCount, Is.EqualTo(1));
            Assert.That(ring.Drain(drain), Is.EqualTo(4));
            Assert.That(drain.DatagramCount, Is.EqualTo(4));
            Assert.That(ring.PendingDatagramCount, Is.Zero);
        }

        [Test]
        public void Abort_AllowsReservationAgain_AndDrainDoesNotTouchReserved()
        {
            var ring = new OscDatagramRing(Options, new OscReceiveDiagnostics());
            var drain = new OscDrainBuffer(Options);

            Assert.That(ring.TryReserveSlot(out int committed), Is.True);
            ring.GetSlotBytes(committed)[0] = 7;
            ring.Commit(committed, 1, 0, 1);
            Assert.That(ring.TryReserveSlot(out int reserved), Is.True);
            ring.GetSlotBytes(reserved)[0] = 9;

            Assert.That(ring.Drain(drain), Is.EqualTo(1));
            Assert.That(ring.PendingDatagramCount, Is.Zero);
            ring.Abort(reserved);
            Assert.That(ring.TryReserveSlot(out int reused), Is.True);
            ring.Abort(reused);
        }

        [Test]
        public void CommitExternal_ParsesClassifiesAndReconstructsMessageView()
        {
            var diagnostics = new OscReceiveDiagnostics();
            var ring = new OscDatagramRing(Options, diagnostics);
            var drain = new OscDrainBuffer(Options);
            var table = new OscAddressKeyTable.Builder(new System.Collections.Generic.Dictionary<string, byte[]>())
                .SetMappings(new[] { new OscMapping("/face", "Face", "layer") })
                .Build(7);
            var packet = Message("/face", ",f", Float(0.75f));

            var records = new OscResolvedMessage[Options.DatagramSlotBytes / 16];
            Assert.That(OscMessageClassifier.ParseAndClassify(packet, table, records, diagnostics), Is.EqualTo(1));
            int mismatches = 0;
            long allocated = ManagedAllocationProbe.MeasureAllocatedBytes(() =>
            {
                if (OscMessageClassifier.ParseAndClassify(packet, table, records, diagnostics) != 1) mismatches++;
            }, 100);
            Assert.That(mismatches, Is.EqualTo(0));
            Assert.That(allocated, Is.EqualTo(0));

            ring.CommitExternal(packet, table);
            Assert.That(ring.Drain(drain), Is.EqualTo(1));
            Assert.That(drain.RecordCount, Is.EqualTo(1));
            ref readonly var record = ref drain.GetRecord(0);
            Assert.That(record.MappingIndex, Is.EqualTo(0));
            Assert.That(record.HasFloat, Is.True);
            Assert.That(record.FloatValue, Is.EqualTo(0.75f));
            var view = drain.GetView(0);
            Assert.That(view.Address.SequenceEqual(Encoding.UTF8.GetBytes("/face")), Is.True);
            Assert.That(view.TryGetFirstAsFloat(out var value), Is.True);
            Assert.That(value, Is.EqualTo(0.75f));
        }

        private static byte[] Message(string address, string tags, byte[] argument)
        {
            var result = new System.Collections.Generic.List<byte>();
            AddPaddedString(result, address);
            AddPaddedString(result, tags);
            result.AddRange(argument);
            return result.ToArray();
        }

        private static byte[] Float(float value)
        {
            var bits = BitConverter.SingleToInt32Bits(value);
            return new[] { (byte)(bits >> 24), (byte)(bits >> 16), (byte)(bits >> 8), (byte)bits };
        }

        private static void AddPaddedString(System.Collections.Generic.List<byte> bytes, string value)
        {
            bytes.AddRange(Encoding.UTF8.GetBytes(value));
            bytes.Add(0);
            while ((bytes.Count & 3) != 0) bytes.Add(0);
        }
    }
}
