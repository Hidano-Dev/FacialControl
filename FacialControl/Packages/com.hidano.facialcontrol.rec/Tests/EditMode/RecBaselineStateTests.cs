using System;
using System.Linq;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecBaselineStateTests : SizedTestFixture
    {
        [Test]
        public void Constructor_DuplicateValueProviderSourceId_ThrowsArgumentException()
        {
            var entries = new[]
            {
                new RecBaselineState.ValueProviderEntry("osc", true, new byte[] { 1 }, new[] { 0.25f }),
                new RecBaselineState.ValueProviderEntry("osc", false, new byte[] { 2 }, new[] { 0.75f })
            };

            Assert.Throws<ArgumentException>(() => new RecBaselineState(null, null, entries, null));
        }

        [Test]
        public void TryGetValueProviderEntry_KnownId_ReturnsSingleEntry()
        {
            var baseline = new RecBaselineState(
                null,
                null,
                new[] { new RecBaselineState.ValueProviderEntry("osc", true, new byte[] { 0x05 }, new[] { 0.1f, 0.9f }) },
                new[] { "smile", "angry" });

            Assert.That(baseline.TryGetValueProviderEntry("osc", out RecBaselineState.ValueProviderEntry entry), Is.True);
            Assert.That(entry.IsValid, Is.True);
            Assert.That(entry.MaskBytes.ToArray(), Is.EqualTo(new byte[] { 0x05 }));
            Assert.That(entry.Values.ToArray(), Is.EqualTo(new[] { 0.1f, 0.9f }));
            Assert.That(baseline.ExpressionEntries.ToArray(), Is.EqualTo(new[] { "smile", "angry" }));
        }

        [Test]
        public void TryGetLayerWeight_KnownLayer_ReturnsValue()
        {
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("face", 0.75f) },
                new[] { new InputSourceWeightEntry("face", "osc", 0.25f) });

            Assert.That(baseline.LayerWeightEntries.Count, Is.EqualTo(1));
            Assert.That(baseline.InputSourceWeightEntries.Count, Is.EqualTo(1));
            Assert.That(baseline.TryGetLayerWeight("face", out float layerWeight), Is.True);
            Assert.That(layerWeight, Is.EqualTo(0.75f));
            Assert.That(baseline.TryGetInputSourceWeight("face", "osc", out float slotWeight), Is.True);
            Assert.That(slotWeight, Is.EqualTo(0.25f));
        }

        [Test]
        public void TryGetLayerWeight_UnknownTarget_ReturnsFalse()
        {
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("face", 0.75f) },
                new[] { new InputSourceWeightEntry("face", "osc", 0.25f) });

            Assert.That(baseline.TryGetLayerWeight("eyes", out float layerWeight), Is.False);
            Assert.That(layerWeight, Is.EqualTo(0f));
            Assert.That(baseline.TryGetLayerWeight(null, out _), Is.False);
            Assert.That(baseline.TryGetInputSourceWeight("face", "input", out float slotWeight), Is.False);
            Assert.That(slotWeight, Is.EqualTo(0f));
            Assert.That(baseline.TryGetInputSourceWeight("eyes", "osc", out _), Is.False);
            Assert.That(RecBaselineState.Empty.TryGetLayerWeight("face", out _), Is.False);
        }

        [Test]
        public void Constructor_DuplicateLayerWeight_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("face", 1f), new LayerWeightEntry("face", 0f) },
                null));
        }

        [Test]
        public void Constructor_DuplicateInputSourceWeight_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new RecBaselineState(
                null, null, null, null,
                null,
                new[] { new InputSourceWeightEntry("face", "osc", 1f), new InputSourceWeightEntry("face", "osc", 0f) }));
        }

        [Test]
        public void Constructor_SameSlotOnDifferentLayers_IsAllowed()
        {
            var baseline = new RecBaselineState(
                null, null, null, null,
                null,
                new[] { new InputSourceWeightEntry("face", "osc", 1f), new InputSourceWeightEntry("eyes", "osc", 0f) });

            Assert.That(baseline.InputSourceWeightEntries.Count, Is.EqualTo(2));
        }

        [Test]
        public void Constructor_WeightEntryWithoutName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new RecBaselineState(
                null, null, null, null,
                new[] { new LayerWeightEntry("", 1f) },
                null));
            Assert.Throws<ArgumentException>(() => new RecBaselineState(
                null, null, null, null,
                null,
                new[] { new InputSourceWeightEntry("face", null, 1f) }));
        }
    }
}
