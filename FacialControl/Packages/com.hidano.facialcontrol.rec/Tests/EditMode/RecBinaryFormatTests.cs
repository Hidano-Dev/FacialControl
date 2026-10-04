using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecBinaryFormatTests : SizedTestFixture
    {
        [Test]
        public void SerializeThenRead_AllRecordKinds_RoundTripsTimeline()
        {
            var timeline = CreateTimeline();
            const long startedAtUnixMilliseconds = 1_721_234_567_890L;

            byte[] bytes = RecBinaryFormat.Serialize(timeline, startedAtUnixMilliseconds);

            bool success = RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(result.HasFooter, Is.True);
            Assert.That(result.RecoveredFromTruncatedTail, Is.False);
            Assert.That(result.Header.FormatVersion, Is.EqualTo(RecBinaryFormat.CurrentFormatVersion));
            Assert.That(result.Header.StartedAtUnixMilliseconds, Is.EqualTo(startedAtUnixMilliseconds));
            AssertRoundTrip(timeline, result.Timeline);
        }

        [Test]
        public void TryRead_MissingFooter_RecoversTimeline()
        {
            var timeline = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            Array.Resize(ref bytes, bytes.Length - RecBinaryFormat.FooterRecordSize);

            bool success = RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(result.HasFooter, Is.False);
            Assert.That(result.RecoveredFromTruncatedTail, Is.True);
            AssertRoundTrip(timeline, result.Timeline);
        }

        [Test]
        public void TryRead_TruncatedFooter_RecoversTimeline()
        {
            var timeline = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            Array.Resize(ref bytes, bytes.Length - 2);

            bool success = RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(result.HasFooter, Is.False);
            Assert.That(result.RecoveredFromTruncatedTail, Is.True);
            AssertRoundTrip(timeline, result.Timeline);
        }

        [Test]
        public void TryRead_UnsupportedVersion_ReturnsFalse()
        {
            var timeline = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            bytes[4] = 2;
            bytes[5] = 0;

            bool success = RecBinaryFormat.TryRead(bytes, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("Unsupported REC format version"));
        }

        [Test]
        public void WriteHeader_Always_SetsFullInputBaselineFlag()
        {
            byte[] header = new byte[RecBinaryFormat.HeaderSize];

            RecBinaryFormat.WriteHeader(header, 123L);

            ushort flags = BitConverter.ToUInt16(header, 6);
            Assert.That(flags & (ushort)RecHeaderFlags.FullInputBaseline,
                Is.EqualTo((ushort)RecHeaderFlags.FullInputBaseline));
        }

        [Test]
        public void TryRead_HeaderWithoutFullInputBaselineFlag_ReturnsError()
        {
            byte[] bytes = RecBinaryFormat.Serialize(CreateTimeline(), 123L);
            bytes[6] = 0;
            bytes[7] = 0;

            bool success = RecBinaryFormat.TryRead(bytes, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("lack the required FullInputBaseline bit"));
        }

        [Test]
        public void TryRead_PreCoverageFileWithKinds1To6Only_ReturnsError()
        {
            byte[] bytes = RecBinaryFormat.Serialize(CreateTimeline(), 123L);
            bytes[6] = 0;
            bytes[7] = 0;

            bool success = RecBinaryFormat.TryRead(bytes, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("re-record with the current version"));
        }

        [Test]
        public void TryRead_DuplicateBaselineValueProviderForSameSource_ReturnsError()
        {
            var baseline = new RecBaselineState(
                null, null,
                new[] { new RecBaselineState.ValueProviderEntry("vp", true, new byte[] { 0x01 }, new[] { 0.5f }) },
                null);
            var timeline = new RecTimeline(baseline, Array.Empty<RecEvent>(), new[] { "vp" }, Array.Empty<string>(), 0d);
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);

            int firstBaseline = FindRecord(bytes, (byte)RecEventKind.BaselineValueProvider);
            int baselineSize = 1 + 2 + 1 + 2 + 1 + 2 + 4;
            byte[] duplicated = new byte[bytes.Length + baselineSize];
            Buffer.BlockCopy(bytes, 0, duplicated, 0, firstBaseline + baselineSize);
            Buffer.BlockCopy(bytes, firstBaseline, duplicated, firstBaseline + baselineSize, baselineSize);
            Buffer.BlockCopy(bytes, firstBaseline + baselineSize, duplicated, firstBaseline + baselineSize * 2,
                bytes.Length - (firstBaseline + baselineSize));
            BinaryPrimitives.WriteUInt32LittleEndian(duplicated.AsSpan(duplicated.Length - 4), 3);

            bool success = RecBinaryFormat.TryRead(duplicated, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("Duplicate value-provider source id 'vp'"));
        }

        [TestCase(0x00)]
        [TestCase(0x01)]
        [TestCase(0x02)]
        [TestCase(0x0F)]
        public void TryRead_BaselineValueProviderWithInvalidFlags_ReturnsError(int rawFlags)
        {
            var baseline = new RecBaselineState(
                null, null,
                new[] { new RecBaselineState.ValueProviderEntry("vp", true, new byte[] { 0x01 }, new[] { 0.5f }) },
                null);
            var timeline = new RecTimeline(baseline, Array.Empty<RecEvent>(), new[] { "vp" }, Array.Empty<string>(), 0d);
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            int record = FindRecord(bytes, (byte)RecEventKind.BaselineValueProvider);
            bytes[record + 3] = (byte)rawFlags;

            bool success = RecBinaryFormat.TryRead(bytes, out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("Baseline value-provider record flags"));
        }

        [Test]
        public void Serialize_BaselineExpression_UsesReservedSourceIndexFromIdTable()
        {
            // 基準に VP があると "@expression" は index 0 ではない。kind 11 の source index は予約 ID の実 index を書く。
            var baseline = new RecBaselineState(
                null, null,
                new[] { new RecBaselineState.ValueProviderEntry("vp", true, new byte[] { 0x01 }, new[] { 0.5f }) },
                new[] { "smile" });
            var timeline = new RecTimeline(baseline, Array.Empty<RecEvent>(), new[] { "vp", "@expression" }, new[] { "smile" }, 0d);

            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);

            // イベント無しなので kind 11（5 バイト）は footer の直前。先頭からの kind 走査はペイロード中の同値バイトを拾い得る。
            int record = bytes.Length - RecBinaryFormat.FooterRecordSize - (1 + 2 + 2);
            Assert.That(bytes[record], Is.EqualTo((byte)RecEventKind.BaselineExpression));
            Assert.That(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(record + 1, 2)), Is.EqualTo(1));
            Assert.That(RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error), Is.True, error);
            Assert.That(result.Timeline.Baseline.ExpressionEntries, Is.EqualTo(new[] { "smile" }));
        }

        [TestCase(0x08)]
        [TestCase(0x87)]
        public void TryRead_ValueProviderSampleWithUnknownFlags_ReturnsErrorInsteadOfTruncatedRecovery(int rawFlags)
        {
            var events = new[]
            {
                RecEvent.CreateValueProviderSample(0.1d, 0, RecValueProviderFlags.IsValid | RecValueProviderFlags.HasValues, 1, 0),
                RecEvent.CreateTriggerOn(0.2d, 0, 0),
            };
            var timeline = new RecTimeline(RecBaselineState.Empty, events, new[] { "vp" }, new[] { "smile" }, 0.2d,
                new IReadOnlyList<float>[] { new[] { 0.5f }, Array.Empty<float>() });
            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            int record = FindRecord(bytes, (byte)RecEventKind.ValueProviderSample);
            bytes[record + 11] = (byte)rawFlags;

            bool success = RecBinaryFormat.TryRead(bytes, out _, out string error);

            Assert.That(success, Is.False, "末尾切れ復旧として後続イベントを捨てた timeline で成功してはならない");
            Assert.That(error, Does.Contain("unknown bits"));
        }

        private static int FindRecord(byte[] bytes, byte kind)
        {
            for (int i = RecBinaryFormat.HeaderSize; i < bytes.Length; i++)
            {
                if (bytes[i] == kind)
                {
                    return i;
                }
            }

            throw new AssertionException($"Record kind {kind} was not found.");
        }

        [Test]
        public void RecTimeline_StoresAnalogAxesOutOfLine()
        {
            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.1d, 0, 0),
                RecEvent.CreateAnalogSample(0.2d, 1, 2),
            };

            var timeline = new RecTimeline(
                RecBaselineState.Empty,
                events,
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile" },
                0.2d,
                new[] { Array.Empty<float>(), new[] { 0.5f, -0.25f } });

            Assert.That(timeline.GetAnalogAxes(0).Count, Is.EqualTo(0));
            Assert.That(timeline.GetAnalogAxes(1).ToArray(), Is.EqualTo(new[] { 0.5f, -0.25f }));
        }

        [Test]
        public void SerializeThenRead_ValueProviderAndExpressionKinds_RoundTrip()
        {
            var values = Enumerable.Range(0, 300).Select(i => BitConverter.Int32BitsToSingle(i ^ 0x3f800000)).ToArray();
            var mask = Enumerable.Range(0, 38).Select(i => (byte)(i * 7)).ToArray();
            var baseline = new RecBaselineState(
                null, null,
                new[] { new RecBaselineState.ValueProviderEntry("vp", true, mask, values) },
                new[] { "smile" });
            var events = new[]
            {
                RecEvent.CreateValueProviderSample(0.1d, 0, RecValueProviderFlags.HasMask, 0, 2),
                RecEvent.CreateValueProviderSample(0.2d, 0, RecValueProviderFlags.HasValues, 2, 0),
                RecEvent.CreateExpressionActivate(0.3d, 0, 0),
                RecEvent.CreateExpressionDeactivate(0.4d, 0, 0),
            };
            var timeline = new RecTimeline(baseline, events, new[] { "vp" }, new[] { "smile" }, 0.4d,
                new IReadOnlyList<float>[] { Array.Empty<float>(), new[] { 1f, -2f }, Array.Empty<float>(), Array.Empty<float>() },
                new IReadOnlyList<byte>[] { new byte[] { 0x05, 0x90 }, Array.Empty<byte>(), Array.Empty<byte>(), Array.Empty<byte>() });

            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);
            Assert.That(RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error), Is.True, error);
            Assert.That(result.Timeline.Baseline.ValueProviderEntries.Count, Is.EqualTo(1));
            Assert.That(result.Timeline.Baseline.ValueProviderEntries[0].Values.ToArray(), Is.EqualTo(values));
            Assert.That(result.Timeline.Baseline.ValueProviderEntries[0].MaskBytes.ToArray(), Is.EqualTo(mask));
            Assert.That(result.Timeline.Baseline.ExpressionEntries.ToArray(), Is.EqualTo(new[] { "smile" }));
            Assert.That(result.Timeline.Events.ToArray(), Is.EqualTo(events));
            Assert.That(result.Timeline.GetMaskBytesSpan(0).ToArray(), Is.EqualTo(new byte[] { 0x05, 0x90 }));
            Assert.That(result.Timeline.GetAnalogAxes(1).ToArray(), Is.EqualTo(new[] { 1f, -2f }));
        }

        [Test]
        public void SerializeThenRead_ValueProviderMaskClearedToEmpty_RoundTripsWithZeroValueCount()
        {
            // mask が空集合へ変化したイベントは HasMask | HasValues かつ ValueCount 0 になる。
            // Writer は HasValues があれば count の 2 バイトを書くので、サイズ計算も同じ判定でないと末尾が溢れる。
            var events = new[]
            {
                RecEvent.CreateValueProviderSample(0.1d, 0,
                    RecValueProviderFlags.IsValid | RecValueProviderFlags.HasMask | RecValueProviderFlags.HasValues, 0, 1),
            };
            var timeline = new RecTimeline(RecBaselineState.Empty, events, new[] { "vp" }, Array.Empty<string>(), 0.1d,
                new IReadOnlyList<float>[] { Array.Empty<float>() },
                new IReadOnlyList<byte>[] { new byte[] { 0x00 } });

            byte[] bytes = RecBinaryFormat.Serialize(timeline, 123L);

            Assert.That(bytes.Length, Is.EqualTo(RecBinaryFormat.GetSerializedSize(timeline)));
            Assert.That(RecBinaryFormat.TryRead(bytes, out RecBinaryFormat.ReadResult result, out string error), Is.True, error);
            Assert.That(result.HasFooter, Is.True);
            Assert.That(result.Timeline.Events.ToArray(), Is.EqualTo(events));
            Assert.That(result.Timeline.GetMaskBytesSpan(0).ToArray(), Is.EqualTo(new byte[] { 0x00 }));
            Assert.That(result.Timeline.GetPayloadSpan(0).Length, Is.EqualTo(0));
        }

        private static RecTimeline CreateTimeline()
        {
            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile", "blink" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.75f }),
                });

            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.1d, 0, 0),
                RecEvent.CreateAnalogSample(0.25d, 1, 2),
                RecEvent.CreateTriggerOff(0.5d, 0, 1),
            };

            return new RecTimeline(
                baseline,
                events,
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile", "blink" },
                0.5d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new[] { -0.1f, 0.2f },
                    Array.Empty<float>(),
                });
        }

        private static void AssertRoundTrip(RecTimeline expected, RecTimeline actual)
        {
            Assert.That(actual.SourceIds.ToArray(), Is.EqualTo(expected.SourceIds.ToArray()));
            Assert.That(actual.ExpressionIds.ToArray(), Is.EqualTo(expected.ExpressionIds.ToArray()));
            Assert.That(actual.DurationSeconds, Is.EqualTo(expected.DurationSeconds));
            Assert.That(actual.Events.ToArray(), Is.EqualTo(expected.Events.ToArray()));

            Assert.That(actual.Baseline.TriggerEntries.Count, Is.EqualTo(expected.Baseline.TriggerEntries.Count));
            Assert.That(actual.Baseline.TriggerEntries[0].SourceId, Is.EqualTo(expected.Baseline.TriggerEntries[0].SourceId));
            Assert.That(actual.Baseline.TriggerEntries[0].ExpressionIds.ToArray(), Is.EqualTo(expected.Baseline.TriggerEntries[0].ExpressionIds.ToArray()));

            Assert.That(actual.Baseline.AnalogEntries.Count, Is.EqualTo(expected.Baseline.AnalogEntries.Count));
            Assert.That(actual.Baseline.AnalogEntries[0].SourceId, Is.EqualTo(expected.Baseline.AnalogEntries[0].SourceId));
            Assert.That(actual.Baseline.AnalogEntries[0].Axes.ToArray(), Is.EqualTo(expected.Baseline.AnalogEntries[0].Axes.ToArray()));

            for (int i = 0; i < expected.Events.Count; i++)
            {
                Assert.That(actual.GetAnalogAxes(i).ToArray(), Is.EqualTo(expected.GetAnalogAxes(i).ToArray()));
            }
        }
    }
}
