using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Rec.Adapters.Recording;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecBaselineCaptureTests : SizedTestFixture
    {
        [Test]
        public void Capture_SparseValueProvider_PacksValuesInMaskOrder()
        {
            var registry = new InputSourceRegistry();
            var mask = new BitArray(10);
            mask[0] = true;
            mask[3] = true;
            mask[8] = true;
            var values = new[] { 10f, 20f, 30f, 40f, 50f, 60f, 70f, 80f, 90f, 100f };
            registry.Register(AdapterSlug.Parse("input"), "osc", new SparseValueProvider("input:osc", mask, values));

            RecBaselineState baseline = RecBaselineCapture.Capture(registry, null, 10);

            Assert.That(baseline.TryGetValueProviderEntry("input:osc", out RecBaselineState.ValueProviderEntry entry), Is.True);
            Assert.That(entry.IsValid, Is.True);
            Assert.That(entry.MaskBytes, Is.EqualTo(new byte[] { 0x09, 0x01 }));
            Assert.That(entry.Values, Is.EqualTo(new[] { 10f, 40f, 90f }));
        }

        [Test]
        public void Capture_SparseValueProvider_IsAcceptedByPlaybackSourceAsBaseline()
        {
            var registry = new InputSourceRegistry();
            var mask = new BitArray(4);
            mask[1] = true;
            registry.Register(AdapterSlug.Parse("input"), "osc", new SparseValueProvider("input:osc", mask, new[] { 0f, 0.5f, 0f, 0f }));
            RecBaselineState baseline = RecBaselineCapture.Capture(registry, null, 4);
            baseline.TryGetValueProviderEntry("input:osc", out RecBaselineState.ValueProviderEntry entry);

            var playback = new RecPlaybackValueProviderSource("input:osc", 4, null);
            var maskBytes = new List<byte>(entry.MaskBytes).ToArray();
            var packedValues = new List<float>(entry.Values).ToArray();

            Assert.That(playback.ApplyState(entry.IsValid, maskBytes, packedValues), Is.True);
            Span<float> output = stackalloc float[4];
            Assert.That(playback.TryWriteValues(output), Is.True);
            Assert.That(output.ToArray(), Is.EqualTo(new[] { 0f, 0.5f, 0f, 0f }));
        }

        [Test]
        public void Capture_ValueProviderWithMismatchedBlendShapeCount_IsSkippedWithWarning()
        {
            var registry = new InputSourceRegistry();
            registry.Register(AdapterSlug.Parse("input"), "osc", new SparseValueProvider("input:osc", new BitArray(2, true), new[] { 1f, 2f }));

            UnityEngine.TestTools.LogAssert.Expect(UnityEngine.LogType.Warning, new System.Text.RegularExpressions.Regex("BlendShapeCount"));
            RecBaselineState baseline = RecBaselineCapture.Capture(registry, null, 4);

            Assert.That(baseline.ValueProviderEntries, Is.Empty);
        }

        private sealed class SparseValueProvider : ValueProviderInputSourceBase
        {
            private readonly BitArray _mask;
            private readonly float[] _values;

            public SparseValueProvider(string id, BitArray mask, float[] values)
                : base(InputSourceId.Parse(id), values.Length)
            {
                _mask = mask;
                _values = values;
            }

            public override BitArray ContributeMask => _mask;

            public override bool TryWriteValues(Span<float> output)
            {
                _values.AsSpan().CopyTo(output);
                return true;
            }
        }
    }
}
