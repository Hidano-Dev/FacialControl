using System;
using System.Linq;
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
        public void WeightEntries_AreQueryableAndRejectDuplicateTargets()
        {
            var baseline = new RecBaselineState(
                null, null, null, null,
                new[] { new RecBaselineState.LayerWeightEntry("face", 0.75f) },
                new[] { new RecBaselineState.InputSourceWeightEntry("face", "osc", 0.25f) });

            Assert.That(baseline.TryGetLayerWeight("face", out RecBaselineState.LayerWeightEntry layer), Is.True);
            Assert.That(layer.Weight, Is.EqualTo(0.75f));
            Assert.That(baseline.TryGetInputSourceWeight("face", "osc", out RecBaselineState.InputSourceWeightEntry source), Is.True);
            Assert.That(source.Weight, Is.EqualTo(0.25f));

            Assert.Throws<ArgumentException>(() => new RecBaselineState(
                null, null, null, null,
                new[] { new RecBaselineState.LayerWeightEntry("face", 1f), new RecBaselineState.LayerWeightEntry("face", 0f) }, null));
        }
    }
}
