using System;
using System.Collections;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Rec.Adapters.Playback;
using NUnit.Framework;
using Hidano.FacialControl.Testing;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public sealed class RecPlaybackValueProviderSourceTests : SizedTestFixture
    {
        [Test]
        public void ApplyState_MaskAndDenseValues_ScattersInMaskOrder()
        {
            var source = new RecPlaybackValueProviderSource("input:values", 10, null);

            Assert.That(source.ApplyState(true, new byte[] { 0x85, 0x01 }, new[] { 0.1f, 0.2f, 0.3f, 0.4f }), Is.True);

            Span<float> output = stackalloc float[10];
            Assert.That(source.TryWriteValues(output), Is.True);
            Assert.That(output.ToArray(), Is.EqualTo(new[] { 0.1f, 0f, 0.2f, 0f, 0f, 0f, 0f, 0.3f, 0.4f, 0f }));
            Assert.That(source.ContributeMask[0], Is.True);
            Assert.That(source.ContributeMask[8], Is.True);
        }

        [Test]
        public void ApplyState_ValueCountMismatch_ReturnsFalseAndKeepsPreviousState()
        {
            var source = new RecPlaybackValueProviderSource("input:values", 3, null);
            Assert.That(source.ApplyState(true, new byte[] { 0x05 }, new[] { 1f, 2f }), Is.True);

            Assert.That(source.ApplyState(true, new byte[] { 0x07 }, new[] { 9f }), Is.False);
            Span<float> output = stackalloc float[3];
            source.TryWriteValues(output);
            Assert.That(output.ToArray(), Is.EqualTo(new[] { 1f, 0f, 2f }));
        }

        [Test]
        public void ApplyState_InvalidState_TryWriteReturnsFalse()
        {
            var source = new RecPlaybackValueProviderSource("input:values", 3, null);
            Assert.That(source.ApplyState(false, ReadOnlySpan<byte>.Empty, ReadOnlySpan<float>.Empty), Is.True);

            Span<float> output = stackalloc float[3];
            Assert.That(source.TryWriteValues(output), Is.False);
        }
    }
}
