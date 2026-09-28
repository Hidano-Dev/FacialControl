using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerSourceWeightEntryTests
    {
        private static InputSourceId Id(string value)
        {
            Assert.IsTrue(InputSourceId.TryParse(value, out var id), $"Failed to parse '{value}'");
            return id;
        }

        [Test]
        public void Constructor_StoresAllFiveFields()
        {
            var sourceId = Id("osc");

            var entry = new LayerSourceWeightEntry(
                layerIdx: 2,
                sourceId: sourceId,
                weight: 0.75f,
                isValid: true,
                saturated: false);

            Assert.AreEqual(2, entry.LayerIdx);
            Assert.AreEqual(sourceId, entry.SourceId);
            Assert.AreEqual(0.75f, entry.Weight);
            Assert.IsTrue(entry.IsValid);
            Assert.IsFalse(entry.Saturated);
        }

        [Test]
        public void Equality_SameValues_AreEqual()
        {
            var sourceId = Id("lipsync");

            var a = new LayerSourceWeightEntry(1, sourceId, 0.5f, true, false);
            var b = new LayerSourceWeightEntry(1, sourceId, 0.5f, true, false);

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentLayerIdx_AreNotEqual()
        {
            var sourceId = Id("osc");

            var a = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, false);
            var b = new LayerSourceWeightEntry(1, sourceId, 0.5f, true, false);

            Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equality_DifferentSourceId_AreNotEqual()
        {
            var a = new LayerSourceWeightEntry(0, Id("osc"), 0.5f, true, false);
            var b = new LayerSourceWeightEntry(0, Id("lipsync"), 0.5f, true, false);

            Assert.AreNotEqual(a, b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equality_DifferentWeight_AreNotEqual()
        {
            var sourceId = Id("osc");

            var a = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, false);
            var b = new LayerSourceWeightEntry(0, sourceId, 0.25f, true, false);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Equality_DifferentIsValid_AreNotEqual()
        {
            var sourceId = Id("osc");

            var a = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, false);
            var b = new LayerSourceWeightEntry(0, sourceId, 0.5f, false, false);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void Equality_DifferentSaturated_AreNotEqual()
        {
            var sourceId = Id("osc");

            var a = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, false);
            var b = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, true);

            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void ObjectEquals_MatchesEqualsContract()
        {
            var sourceId = Id("osc");

            var a = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, false);
            object b = new LayerSourceWeightEntry(0, sourceId, 0.5f, true, false);

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals("not an entry"));
            Assert.IsFalse(a.Equals(null));
        }

        [Test]
        public void SnapshotApi_ReceivesEntriesAsSpan()
        {
            var osc = Id("osc");
            var lipsync = Id("lipsync");

            var backing = new LayerSourceWeightEntry[3];
            Span<LayerSourceWeightEntry> snapshot = backing;
            snapshot[0] = new LayerSourceWeightEntry(0, osc, 0.5f, true, false);
            snapshot[1] = new LayerSourceWeightEntry(0, lipsync, 0.25f, true, false);
            snapshot[2] = new LayerSourceWeightEntry(1, osc, 1.0f, true, true);

            Assert.AreEqual(0, snapshot[0].LayerIdx);
            Assert.AreEqual(osc, snapshot[0].SourceId);
            Assert.IsFalse(snapshot[0].Saturated);

            Assert.AreEqual(0, snapshot[1].LayerIdx);
            Assert.AreEqual(lipsync, snapshot[1].SourceId);

            Assert.AreEqual(1, snapshot[2].LayerIdx);
            Assert.IsTrue(snapshot[2].Saturated);
        }

        [Test]
        public void SnapshotApi_ReceivesEntriesAsArray()
        {
            var osc = Id("osc");
            var lipsync = Id("lipsync");

            var snapshot = new[]
            {
                new LayerSourceWeightEntry(0, osc, 0.5f, true, false),
                new LayerSourceWeightEntry(0, lipsync, 0.5f, true, false)
            };

            Assert.AreEqual(2, snapshot.Length);
            Assert.AreEqual(new LayerSourceWeightEntry(0, osc, 0.5f, true, false), snapshot[0]);
            Assert.AreEqual(new LayerSourceWeightEntry(0, lipsync, 0.5f, true, false), snapshot[1]);
        }

        [Test]
        public void Default_HasZeroedFields()
        {
            var entry = default(LayerSourceWeightEntry);

            Assert.AreEqual(0, entry.LayerIdx);
            Assert.AreEqual(default(InputSourceId), entry.SourceId);
            Assert.AreEqual(0f, entry.Weight);
            Assert.IsFalse(entry.IsValid);
            Assert.IsFalse(entry.Saturated);
        }
    }
}
