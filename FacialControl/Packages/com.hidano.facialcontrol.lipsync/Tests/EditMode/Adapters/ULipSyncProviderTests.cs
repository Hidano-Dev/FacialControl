using System;
using System.Collections;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    // volume 正規化・SmoothDamp・sum=1 正規化は uLipSync 公式（IPhonemeWeightSource 実装）へ
    // 委譲済み。本 provider の責務は「source から読んだ確定 weight × volume を snapshot へ適用する」
    // ことのみ。テストは FakePhonemeWeightSource で weight/volume を直接与えて合成結果を検証する。
    [TestFixture]
    [SmallTest]
    public class ULipSyncProviderTests : SizedTestFixture
    {
        private const float Tolerance = 1e-4f;

        [Test]
        public void Constructor_NullSource_ThrowsArgumentNullException()
        {
            Assert.That(
                () => new ULipSyncProvider(null, Array.Empty<PhonemeSnapshot>(), 1),
                Throws.ArgumentNullException);
        }

        [Test]
        public void TryComposePhonemeWeights_KnownPhonemeId_WritesWeightTimesVolumeScaledSnapshot()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 3,
                Snapshot("A", 0.5f, 0.25f, 0f),
                Snapshot("I", 0f, 0.4f, 0.8f));
            var output = new float[3];

            source.SetFrame(0.5f, ("A", 0.75f), ("I", 0.25f));
            bool composed = provider.TryComposePhonemeWeights("A", output);

            Assert.That(composed, Is.True);
            // factor = weight(0.75) * volume(0.5) = 0.375 を snapshot_A に適用。
            AssertValuesClose(output, 0.375f * 0.5f, 0.375f * 0.25f, 0f);
        }

        [Test]
        public void GetLipSyncValues_MultiplePhonemes_AccumulatesWeightedValues()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 3,
                Snapshot("A", 0.5f, 0f, 0.25f),
                Snapshot("I", 0.2f, 0.4f, 0f));
            var output = new float[3];

            source.SetFrame(0.5f, ("A", 0.8f), ("I", 0.2f));
            provider.GetLipSyncValues(output);

            // factorA = 0.8*0.5 = 0.4, factorI = 0.2*0.5 = 0.1
            AssertValuesClose(output,
                0.4f * 0.5f + 0.1f * 0.2f,
                0.4f * 0f + 0.1f * 0.4f,
                0.4f * 0.25f + 0.1f * 0f);
        }

        [Test]
        public void GetLipSyncValues_PhonemeAbsentInSource_ContributesZero()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 3,
                Snapshot("A", 0.2f, 0.3f, 0f),
                Snapshot("E", 0f, 0.5f, 1f));
            var output = new float[3];

            // E は source に無い (weight=0) ので寄与ゼロ。A 単独。
            source.SetFrame(0.75f, ("A", 1f));
            provider.GetLipSyncValues(output);

            AssertValuesClose(output, 0.75f * 0.2f, 0.75f * 0.3f, 0f);
        }

        [Test]
        public void GetLipSyncValues_ZeroVolume_ProducesZero()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(source, 3, Snapshot("A", 1f, 0.5f, 0.25f));
            var output = new float[3];

            source.SetFrame(0f, ("A", 1f));
            provider.GetLipSyncValues(output);

            AssertValuesClose(output, 0f, 0f, 0f);
        }

        [Test]
        public void TryComposePhonemeWeights_UnknownPhonemeId_ReturnsFalse()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(source, 2, Snapshot("A", 1f, 0f));
            var output = new[] { 0.25f, 0.5f };

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Phoneme 'Unknown' is not registered"));
            bool composed = provider.TryComposePhonemeWeights("Unknown", output);

            Assert.That(composed, Is.False);
            AssertValuesClose(output, 0.25f, 0.5f);
        }

        [Test]
        public void TryGetPhonemeIndex_KnownPhonemeId_ReturnsTrueWithIndex()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 2,
                Snapshot("A", 1f, 0f),
                Snapshot("I", 0f, 1f));

            bool found = provider.TryGetPhonemeIndex("I", out int index);

            Assert.That(found, Is.True);
            Assert.That(index, Is.EqualTo(1));
        }

        [Test]
        public void TryGetPhonemeIndex_UnknownPhonemeId_ReturnsFalse()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(source, 1, Snapshot("A", 1f));

            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("Phoneme 'Unknown' is not registered"));
            bool found = provider.TryGetPhonemeIndex("Unknown", out int index);

            Assert.That(found, Is.False);
            Assert.That(index, Is.EqualTo(-1));
        }

        [Test]
        public void GetPhonemeContributeMask_KnownPhonemeId_ReturnsNonNullBitArray()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 4,
                Snapshot("A", 1f, 0f, 0.5f, 0f),
                Snapshot("I", 0f, 0.25f, 0f, 0f));

            BitArray mask = provider.GetPhonemeContributeMask("A");

            Assert.That(mask, Is.Not.Null);
            Assert.That(mask.Length, Is.EqualTo(4));
            Assert.That(mask[0], Is.True);
            Assert.That(mask[1], Is.False);
            Assert.That(mask[2], Is.True);
            Assert.That(mask[3], Is.False);
        }

        [Test]
        public void ContributeMask_AllNonZeroPhonemeWeightIndexes_AreIncludedInUnionMask()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 5,
                Snapshot("A", 1f, 0f, 0.25f, 0f, 0f),
                Snapshot("I", 0f, 0.5f, 0f, 0f, 0f),
                Snapshot("O", 0f, 0f, 0f, 0.75f, 0f));

            BitArray mask = provider.ContributeMask;

            Assert.That(mask[0], Is.True);
            Assert.That(mask[1], Is.True);
            Assert.That(mask[2], Is.True);
            Assert.That(mask[3], Is.True);
            Assert.That(mask[4], Is.False);
        }

        [Test]
        public void ContributeMask_AfterConstruction_LengthMatchesBlendShapeCount()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 4,
                Snapshot("A", 1f, 0f),
                Snapshot("I", 0f, 0f, 0.5f, 0f));

            Assert.That(provider.ContributeMask.Length, Is.EqualTo(4));
        }

        [Test]
        public void ContributeMask_DuringRuntime_ReturnsSameReference()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(
                source, 3,
                Snapshot("A", 1f, 0f, 0f),
                Snapshot("I", 0f, 0.5f, 0.25f));
            var output = new float[3];

            BitArray first = provider.ContributeMask;

            source.SetFrame(1f, ("A", 1f));
            provider.GetLipSyncValues(output);
            BitArray afterFrame = provider.ContributeMask;

            provider.RequestZeroOutputForNextFrame();
            provider.GetLipSyncValues(output);
            BitArray afterZeroRequest = provider.ContributeMask;

            Assert.That(afterFrame, Is.SameAs(first));
            Assert.That(afterZeroRequest, Is.SameAs(first));
        }

        [Test]
        public void BlendShapeNames_AfterConstruction_ReturnsFixedOrder()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(source, 3, Snapshot("A", 1f, 0f, 0f));

            var first = provider.BlendShapeNames.ToArray();
            var second = provider.BlendShapeNames.ToArray();

            Assert.That(first, Has.Length.EqualTo(3));
            Assert.That(second, Is.EqualTo(first));
        }

        [Test]
        public void RequestZeroOutputForNextFrame_ProducesZeroSpanThenResumes()
        {
            var source = new FakePhonemeWeightSource();
            using var provider = CreateProvider(source, 3, Snapshot("A", 0.4f, 0.2f, 0.1f));
            var output = new float[3];

            source.SetFrame(1f, ("A", 1f));
            provider.RequestZeroOutputForNextFrame();
            provider.GetLipSyncValues(output);
            AssertValuesClose(output, 0f, 0f, 0f);

            // zero settle は 1 フレームのみ。次回は source の確定値がそのまま読める。
            provider.GetLipSyncValues(output);
            AssertValuesClose(output, 0.4f, 0.2f, 0.1f);
        }

        [Test]
        public void Dispose_CalledTwice_DoesNotThrow()
        {
            var source = new FakePhonemeWeightSource();
            var provider = CreateProvider(source, 1, Snapshot("A", 1f));

            Assert.That(
                () =>
                {
                    provider.Dispose();
                    provider.Dispose();
                },
                Throws.Nothing);
        }

        // ---- ヘルパ ---------------------------------

        private static ULipSyncProvider CreateProvider(
            IPhonemeWeightSource source,
            int blendShapeCount,
            params PhonemeSnapshot[] snapshots)
        {
            return new ULipSyncProvider(source, snapshots, blendShapeCount);
        }

        private static PhonemeSnapshot Snapshot(string phonemeId, params float[] weights)
        {
            return new PhonemeSnapshot(phonemeId, weights);
        }

        private static void AssertValuesClose(float[] actual, params float[] expected)
        {
            Assert.That(actual.Length, Is.EqualTo(expected.Length));
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(Tolerance), "index " + i);
            }
        }
    }
}
