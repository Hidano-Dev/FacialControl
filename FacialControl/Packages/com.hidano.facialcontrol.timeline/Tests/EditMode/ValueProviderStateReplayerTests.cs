using System;
using Hidano.FacialControl.Rec.Adapters.Playback;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Services;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class ValueProviderStateReplayerTests : SizedTestFixture
    {
        [Test]
        public void Apply_MaskAndValues_SetsMaskedValuesInMaskOrder()
        {
            var replayer = new ValueProviderStateReplayer(8);

            bool applied = replayer.Apply(true, new byte[] { 0b0000_0101 }, new[] { 0.25f, 0.75f });

            Assert.That(applied, Is.True);
            Assert.That(replayer.IsValid, Is.True);
            Assert.That(replayer.Contributes(0), Is.True);
            Assert.That(replayer.Contributes(1), Is.False);
            Assert.That(replayer.Contributes(2), Is.True);
            Assert.That(replayer.GetValue(0), Is.EqualTo(0.25f));
            Assert.That(replayer.GetValue(2), Is.EqualTo(0.75f));
        }

        [Test]
        public void Apply_ValuesOnly_KeepsMaskAndUpdatesValues()
        {
            var replayer = new ValueProviderStateReplayer(8);
            replayer.Apply(true, new byte[] { 0b0000_0011 }, new[] { 0.1f, 0.2f });

            bool applied = replayer.Apply(true, ReadOnlySpan<byte>.Empty, new[] { 0.5f, 0.6f });

            Assert.That(applied, Is.True);
            Assert.That(replayer.GetValue(0), Is.EqualTo(0.5f));
            Assert.That(replayer.GetValue(1), Is.EqualTo(0.6f));
        }

        [Test]
        public void Apply_ValidityOnly_KeepsMaskAndValues()
        {
            var replayer = new ValueProviderStateReplayer(8);
            replayer.Apply(true, new byte[] { 0b0000_0001 }, new[] { 0.4f });

            bool applied = replayer.Apply(false, ReadOnlySpan<byte>.Empty, ReadOnlySpan<float>.Empty);

            Assert.That(applied, Is.True);
            Assert.That(replayer.IsValid, Is.False);
            Assert.That(replayer.Contributes(0), Is.True);
            Assert.That(replayer.GetValue(0), Is.EqualTo(0.4f));
        }

        [Test]
        public void Apply_NewMask_ClearsValuesOutsideNewMask()
        {
            var replayer = new ValueProviderStateReplayer(8);
            replayer.Apply(true, new byte[] { 0b0000_0011 }, new[] { 0.1f, 0.2f });

            replayer.Apply(true, new byte[] { 0b0000_0100 }, new[] { 0.9f });

            Assert.That(replayer.Contributes(0), Is.False);
            Assert.That(replayer.GetValue(0), Is.EqualTo(0f));
            Assert.That(replayer.GetValue(1), Is.EqualTo(0f));
            Assert.That(replayer.GetValue(2), Is.EqualTo(0.9f));
        }

        [Test]
        public void Apply_ValueCountMismatch_RejectsAndKeepsState()
        {
            var replayer = new ValueProviderStateReplayer(8);
            replayer.Apply(true, new byte[] { 0b0000_0001 }, new[] { 0.3f });

            bool applied = replayer.Apply(false, ReadOnlySpan<byte>.Empty, new[] { 0.1f, 0.2f });

            Assert.That(applied, Is.False);
            Assert.That(replayer.IsValid, Is.True);
            Assert.That(replayer.GetValue(0), Is.EqualTo(0.3f));
        }

        [Test]
        public void Apply_MaskLengthMismatch_Rejects()
        {
            var replayer = new ValueProviderStateReplayer(8);

            bool applied = replayer.Apply(true, new byte[] { 1, 0 }, new[] { 0.3f });

            Assert.That(applied, Is.False);
            Assert.That(replayer.IsValid, Is.False);
        }

        [Test]
        public void Apply_SameSequenceAsRecPlaybackSource_ProducesSameWrittenValues()
        {
            // REC 再生の注入ソースと同じ規則で状態を復元できること（Export と REC 再生の一致の根拠）。
            const int blendShapeCount = 10;
            var replayer = new ValueProviderStateReplayer(16);
            var recSource = new RecPlaybackValueProviderSource("ifm", blendShapeCount, null);
            (bool valid, byte[] mask, float[] values)[] records =
            {
                (true, new byte[] { 0b0000_0110, 0b0000_0010 }, new[] { 0.1f, 0.2f, 0.3f }),
                (true, Array.Empty<byte>(), new[] { 0.4f, 0.5f, 0.6f }),
                (false, Array.Empty<byte>(), Array.Empty<float>()),
                (true, new byte[] { 0b0000_0001, 0b0000_0000 }, new[] { 0.7f }),
                (true, Array.Empty<byte>(), new[] { 0.8f, 0.9f }),
            };

            var expected = new float[blendShapeCount];
            for (int r = 0; r < records.Length; r++)
            {
                bool replayed = replayer.Apply(records[r].valid, records[r].mask, records[r].values);
                bool injected = recSource.ApplyState(records[r].valid, records[r].mask, records[r].values);
                Assert.That(replayed, Is.EqualTo(injected), $"record {r}: 受理 / 拒否が REC 再生と一致する");

                Array.Clear(expected, 0, expected.Length);
                bool wrote = recSource.TryWriteValues(expected);
                Assert.That(replayer.IsValid, Is.EqualTo(wrote), $"record {r}: 有効状態");
                for (int i = 0; i < blendShapeCount; i++)
                {
                    Assert.That(replayer.Contributes(i), Is.EqualTo(recSource.ContributeMask[i]), $"record {r}: mask[{i}]");
                    if (wrote && recSource.ContributeMask[i])
                    {
                        Assert.That(replayer.GetValue(i), Is.EqualTo(expected[i]), $"record {r}: value[{i}]");
                    }
                }
            }
        }
    }
}
