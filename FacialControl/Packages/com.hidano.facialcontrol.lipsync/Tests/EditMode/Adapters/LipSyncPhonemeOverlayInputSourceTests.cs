using System;
using System.Collections;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Tests.Shared;
using Hidano.FacialControl.Tests.Shared;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class LipSyncPhonemeOverlayInputSourceTests : SizedTestFixture
    {
        private const int BlendShapeCount = 3;

        [Test]
        public void Constructor_NullProvider_Throws()
        {
            Assert.That(
                () => new LipSyncPhonemeOverlayInputSource(
                    InputSourceId.Parse("lipsync-overlay:a"),
                    "A",
                    null,
                    BlendShapeCount),
                Throws.ArgumentNullException);
        }

        [Test]
        public void TryWriteValues_PhonemeNotRegistered_ReturnsFalse()
        {
            using var provider = CreateProvider(new FakePhonemeWeightSource(), Snapshot("A", 1f, 0f, 0f));
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("'Unknown'"));
            var source = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse("lipsync-overlay:unknown"),
                "Unknown",
                provider,
                BlendShapeCount);
            var output = new[] { 0.1f, 0.2f, 0.3f };

            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.False);
            Assert.That(output, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }));
        }

        [Test]
        public void TryWriteValues_ProviderActiveWithVolume_WritesScaledWeights()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 0.5f, 0.25f, 0f));
            var source = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse("lipsync-overlay:a"),
                "A",
                provider,
                BlendShapeCount);
            var output = new float[BlendShapeCount];

            // weight=1（uLipSync 委譲後の確定値）, volume=0.8 → factor=0.8。
            weightSource.SetFrame(0.8f, ("A", 1f));
            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.True);
            AssertValuesClose(output, 0.8f * 0.5f, 0.8f * 0.25f, 0f);
        }

        [Test]
        public void TryWriteValues_ProviderSilent_ReturnsFalse()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 1f, 0.5f, 0.25f));
            var source = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse("lipsync-overlay:a"),
                "A",
                provider,
                BlendShapeCount);
            var output = new[] { 0.1f, 0.2f, 0.3f };

            // volume=0 → factor=0 → sum < SilenceThreshold → false。
            weightSource.SetFrame(0f, ("A", 1f));
            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.False);
            Assert.That(output, Is.EqualTo(new[] { 0.1f, 0.2f, 0.3f }));
        }

        [Test]
        public void ContributeMask_AfterConstruction_MatchesProviderMask()
        {
            using var provider = CreateProvider(
                new FakePhonemeWeightSource(),
                Snapshot("A", 1f, 0f, 0.25f),
                Snapshot("I", 0f, 0.5f, 0f));
            var source = new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse("lipsync-overlay:a"),
                "A",
                provider,
                BlendShapeCount);

            BitArray mask = source.ContributeMask;

            Assert.That(mask.Length, Is.EqualTo(BlendShapeCount));
            Assert.That(mask[0], Is.True);
            Assert.That(mask[1], Is.False);
            Assert.That(mask[2], Is.True);
        }

        #region 発話ゲート（出力停止・口まわりの押さえ込み）

        [Test]
        public void TryWriteValues_ProviderOutputDisabled_ReturnsFalse()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 0.5f, 0f, 0f));
            var source = CreateSource(provider);
            var output = new float[BlendShapeCount];

            weightSource.SetFrame(1f, ("A", 1f));
            provider.OutputEnabled = false;
            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.False, "ゲートが閉じている間は下位レイヤーをそのまま通す。");
        }

        [Test]
        public void TryWriteValues_SilentWhileSuppressActive_WritesZeroOnSuppressIndicesOnly()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 0.5f, 0f, 0f));
            provider.SetSuppressIndices(new[] { 2 });
            var source = CreateSource(provider);
            var output = new[] { 0.3f, 0.3f, 0.3f };

            weightSource.SetFrame(0f, ("A", 0f));
            provider.SuppressActive = true;
            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.True, "発話中は無音フレームでもキャプチャの口を押さえ込む。");
            Assert.That(output[2], Is.EqualTo(0f));
            Assert.That(source.ContributeMask[0], Is.False, "無音時は母音の BlendShape を書き込み対象に含めない。");
            Assert.That(source.ContributeMask[1], Is.False);
            Assert.That(source.ContributeMask[2], Is.True);
        }

        [Test]
        public void TryWriteValues_SilentWhileSuppressInactive_ReturnsFalse()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 0.5f, 0f, 0f));
            provider.SetSuppressIndices(new[] { 2 });
            var source = CreateSource(provider);
            var output = new float[BlendShapeCount];

            weightSource.SetFrame(0f, ("A", 0f));
            provider.SuppressActive = false;
            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.False);
        }

        [Test]
        public void TryWriteValues_SpeakingWhileSuppressActive_IncludesSuppressIndicesInMask()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 0.5f, 0f, 0f));
            provider.SetSuppressIndices(new[] { 0, 2 });
            var source = CreateSource(provider);
            var output = new float[BlendShapeCount];

            weightSource.SetFrame(1f, ("A", 1f));
            provider.SuppressActive = true;
            bool written = source.TryWriteValues(output);

            Assert.That(written, Is.True);
            AssertValuesClose(output, 0.5f, 0f, 0f);
            Assert.That(source.ContributeMask[0], Is.True, "母音側が同じ BlendShape を動かす場合はその値を書く。");
            Assert.That(source.ContributeMask[1], Is.False);
            Assert.That(source.ContributeMask[2], Is.True);
        }

        [Test]
        public void TryWriteValues_SpeakingWhileSuppressInactive_UsesPhonemeMaskOnly()
        {
            var weightSource = new FakePhonemeWeightSource();
            using var provider = CreateProvider(weightSource, Snapshot("A", 0.5f, 0f, 0f));
            provider.SetSuppressIndices(new[] { 2 });
            var source = CreateSource(provider);
            var output = new float[BlendShapeCount];

            weightSource.SetFrame(1f, ("A", 1f));
            provider.SuppressActive = false;
            source.TryWriteValues(output);

            Assert.That(source.ContributeMask[0], Is.True);
            Assert.That(source.ContributeMask[2], Is.False);
        }

        private static LipSyncPhonemeOverlayInputSource CreateSource(ULipSyncProvider provider)
        {
            return new LipSyncPhonemeOverlayInputSource(
                InputSourceId.Parse("lipsync-overlay:a"),
                "A",
                provider,
                BlendShapeCount);
        }

        #endregion

        private static ULipSyncProvider CreateProvider(
            FakePhonemeWeightSource source,
            params PhonemeSnapshot[] snapshots)
        {
            return new ULipSyncProvider(source, snapshots, BlendShapeCount);
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
                Assert.That(actual[i], Is.EqualTo(expected[i]).Within(1e-4f), "index " + i);
            }
        }
    }
}
