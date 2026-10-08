using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Adapters.Recording;
using Hidano.FacialControl.Rec.Application.UseCases;
using Hidano.FacialControl.Rec.Domain.Interfaces;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [MediumTest]
    public class RecStreamWriterTests : SizedTestFixture
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FacialControlRecStreamWriterTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDirectory);
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_tempDirectory) && Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, true);
            }
        }

        [Test]
        public void Complete_WritesReadableFileWithBaselineAndRuntimeEvents()
        {
            string filePath = Path.Combine(_tempDirectory, "recording.fcrec");
            var baseline = new RecBaselineState(
                new[] { new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }) },
                new[] { new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.5f }) });

            using var writer = new RecStreamWriter(filePath, segmentCapacity: 2, initialSegments: 2, axisFloatCapacityPerSegment: 8);
            writer.Open(baseline);
            writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
            writer.AppendEvent(RecEvent.CreateAnalogSample(0.2d, 1, 2), new float[] { 0.4f, -0.75f });

            writer.Complete(0.2d, 2);

            bool success = RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result);

            Assert.That(success, Is.True);
            Assert.That(result, Is.Not.Null);
            // 系1の予約 source は baseline に無くても必ず IdDefine される（RecordingUseCase のシードと一致させる）。
            Assert.That(result.Timeline.SourceIds, Is.EqualTo(new[] { "input:trigger", "input:gaze", "@expression" }));
            Assert.That(result.Timeline.ExpressionIds, Is.EqualTo(new[] { "smile" }));
            Assert.That(result.Timeline.Baseline.TryGetTriggerStack("input:trigger", out IReadOnlyList<string> triggerStack), Is.True);
            Assert.That(triggerStack, Is.EqualTo(new[] { "smile" }));
            Assert.That(result.Timeline.Baseline.TryGetAnalogAxes("input:gaze", out IReadOnlyList<float> analogAxes), Is.True);
            Assert.That(analogAxes, Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(result.Timeline.Events.Count, Is.EqualTo(2));
            Assert.That(result.Timeline.Events[0], Is.EqualTo(RecEvent.CreateTriggerOn(0.1d, 0, 0)));
            Assert.That(result.Timeline.Events[1], Is.EqualTo(RecEvent.CreateAnalogSample(0.2d, 1, 2)));
            Assert.That(result.Timeline.GetAnalogAxes(1), Is.EqualTo(new[] { 0.4f, -0.75f }));
        }

        [Test]
        public void Complete_WritesValueProviderAndExpressionBaselinesBeforeRuntimeEvents()
        {
            string filePath = Path.Combine(_tempDirectory, "full-baseline.fcrec");
            var baseline = new RecBaselineState(
                Array.Empty<RecBaselineState.TriggerEntry>(),
                Array.Empty<RecBaselineState.AnalogEntry>(),
                new[] { new RecBaselineState.ValueProviderEntry("input:face", true, new byte[] { 0x05 }, new[] { 0.25f, -0.5f }) },
                new[] { "smile" });

            using var writer = new RecStreamWriter(filePath, segmentCapacity: 2, initialSegments: 2,
                axisFloatCapacityPerSegment: 8, byteCapacityPerSegment: 1);
            writer.Open(baseline);
            writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
            writer.Complete(0.1d, 1);

            Assert.That(RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result), Is.True);
            Assert.That(result.Timeline.Baseline.TryGetValueProviderEntry("input:face",
                out RecBaselineState.ValueProviderEntry valueProvider), Is.True);
            Assert.That(valueProvider.IsValid, Is.True);
            Assert.That(valueProvider.MaskBytes, Is.EqualTo(new byte[] { 0x05 }));
            Assert.That(valueProvider.Values, Is.EqualTo(new[] { 0.25f, -0.5f }));
            Assert.That(result.Timeline.Baseline.ExpressionEntries, Is.EqualTo(new[] { "smile" }));
            Assert.That(result.Timeline.Events, Is.EqualTo(new[] { RecEvent.CreateTriggerOn(0.1d, 0, 0) }));
        }

        [Test]
        public void Complete_BaselineWithBlendShapeNames_ReadsBackNamesInIndexOrder()
        {
            string filePath = Path.Combine(_tempDirectory, "blendshape-names.fcrec");
            RecBaselineState baseline = new RecBaselineState(
                    Array.Empty<RecBaselineState.TriggerEntry>(),
                    Array.Empty<RecBaselineState.AnalogEntry>(),
                    new[] { new RecBaselineState.ValueProviderEntry("ifm", true, new byte[] { 0x04 }, new[] { 0.5f }) },
                    null)
                .WithBlendShapeNames(new[] { "ex_agosage", "browInnerUp", "jawOpen" });

            using var writer = new RecStreamWriter(filePath, segmentCapacity: 2, initialSegments: 2,
                axisFloatCapacityPerSegment: 8, byteCapacityPerSegment: 1);
            writer.Open(baseline);
            writer.Complete(0.1d, 0);

            Assert.That(RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result), Is.True);
            Assert.That(result.Timeline.Baseline.BlendShapeNames, Is.EqualTo(new[] { "ex_agosage", "browInnerUp", "jawOpen" }));
            Assert.That(result.Timeline.Baseline.TryGetValueProviderEntry("ifm", out _), Is.True);
        }

        [Test]
        public void Complete_ExpressionActivationThenNewSource_ReadsBackWithRecordingUseCaseIdOrder()
        {
            // RecordingUseCase は系1の予約 source "@expression" をシード済みとして IdDefine を出さない。
            // ライター側の IdDefine も同じシードを書かないと、後から初登場した入力源の index が 1 つずれて
            // 読み戻しが "Id values must be non-empty" で失敗する（PR #46 の赤 17 件の経路）。
            string filePath = Path.Combine(_tempDirectory, "expression-then-source.fcrec");
            using var writer = new RecStreamWriter(filePath, segmentCapacity: 2, initialSegments: 2, axisFloatCapacityPerSegment: 8);
            var clock = new StubClock();
            using var useCase = new RecordingUseCase(new NoopObservationBus(), clock, writer);

            useCase.StartRecording(RecBaselineState.Empty);
            useCase.OnExpressionActivated("@expression", "smile");
            clock.ElapsedSeconds = 0.1d;
            useCase.OnTriggerOn("input:trigger", "angry");
            clock.ElapsedSeconds = 0.2d;
            useCase.StopRecording();

            Assert.That(RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result), Is.True);
            Assert.That(result.Timeline.SourceIds, Is.EqualTo(new[] { "@expression", "input:trigger" }));
            Assert.That(result.Timeline.ExpressionIds, Is.EqualTo(new[] { "smile", "angry" }));
            Assert.That(result.Timeline.Events.Count, Is.EqualTo(2));
            Assert.That(result.Timeline.Events[0], Is.EqualTo(RecEvent.CreateExpressionActivate(0d, 0, 0)));
            Assert.That(result.Timeline.Events[1], Is.EqualTo(RecEvent.CreateTriggerOn(0.1d, 1, 1)));
        }

        [Test]
        public void Complete_WhenCalledTwice_IsQuietNoOp()
        {
            string filePath = Path.Combine(_tempDirectory, "noop.fcrec");
            using var writer = new RecStreamWriter(filePath);
            writer.Open(RecBaselineState.Empty);

            writer.Complete(0d, 0);
            writer.Complete(0d, 0);

            Assert.That(File.Exists(filePath), Is.True);
        }

        [Test]
        public void Complete_AfterSuccessfulOpen_ReportsTheOpenedPath()
        {
            string filePath = Path.Combine(_tempDirectory, "opened.fcrec");

            using var writer = new RecStreamWriter(filePath);
            writer.Open(RecBaselineState.Empty);
            writer.Complete(0d, 0);

            Assert.That(writer.OutputFilePath, Is.EqualTo(filePath));
            Assert.That(writer.HasOutputFailed, Is.False);
        }

        [Test]
        public void Open_WhenFileAlreadyExists_WritesToSuffixedPathAndLeavesExistingFileUntouched()
        {
            string filePath = Path.Combine(_tempDirectory, "existing.fcrec");
            byte[] original = { 1, 2, 3, 4 };
            File.WriteAllBytes(filePath, original);

            // 読み戻して検証するため、イベントが参照する source / expression（index 0）を baseline で定義する。
            // 空 baseline のままだと未定義 index を参照するファイルになり、リーダーが正しく拒否する。
            var baseline = new RecBaselineState(
                new[] { new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }) },
                Array.Empty<RecBaselineState.AnalogEntry>());

            using (var writer = new RecStreamWriter(filePath))
            {
                writer.Open(baseline);
                writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
                writer.Complete(0.1d, 1);

                Assert.That(writer.OutputFilePath, Is.EqualTo(Path.Combine(_tempDirectory, "existing-2.fcrec")));
                Assert.That(RecFileReader.TryRead(writer.OutputFilePath, out RecBinaryFormat.ReadResult result), Is.True);
                Assert.That(result.Timeline.SourceIds, Is.EqualTo(new[] { "input:trigger", "@expression" }));
                Assert.That(result.Timeline.ExpressionIds, Is.EqualTo(new[] { "smile" }));
                Assert.That(result.Timeline.Events, Is.EqualTo(new[] { RecEvent.CreateTriggerOn(0.1d, 0, 0) }));
            }

            Assert.That(File.ReadAllBytes(filePath), Is.EqualTo(original));
        }

        [Test]
        public void Open_TwoWritersRacingForTheSamePath_KeepBothTakes()
        {
            string filePath = Path.Combine(_tempDirectory, "race.fcrec");
            using var bothResolved = new Barrier(2);
            int factoryCalls = 0;

            // 両ライターが同じパスを解決し終えてから CreateNew させ、衝突を必ず起こす。
            Func<string, Stream> factory = path =>
            {
                if (Interlocked.Increment(ref factoryCalls) <= 2)
                {
                    bothResolved.SignalAndWait(TimeSpan.FromSeconds(2d));
                }

                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            };

            using var first = new RecStreamWriter(filePath, 2, 2, 8, factory, null);
            using var second = new RecStreamWriter(filePath, 2, 2, 8, factory, null);
            first.Open(RecBaselineState.Empty);
            second.Open(RecBaselineState.Empty);
            first.Complete(0d, 0);
            second.Complete(0d, 0);

            Assert.That(first.HasOutputFailed, Is.False);
            Assert.That(second.HasOutputFailed, Is.False);
            Assert.That(
                new[] { first.OutputFilePath, second.OutputFilePath },
                Is.EquivalentTo(new[] { filePath, Path.Combine(_tempDirectory, "race-2.fcrec") }));
        }

        [Test]
        public void Open_WhenStreamCannotBeCreated_ReportsFailureAndDropsEvents()
        {
            string filePath = Path.Combine(_tempDirectory, "unavailable.fcrec");

            LogAssert.Expect(LogType.Error, new Regex("could not open"));

            using var writer = new RecStreamWriter(
                filePath,
                segmentCapacity: 2,
                initialSegments: 2,
                axisFloatCapacityPerSegment: 8,
                streamFactory: _ => throw new IOException("disk unavailable"),
                postFinalizeAction: null);

            writer.Open(RecBaselineState.Empty);
            writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
            writer.Complete(0.1d, 1);

            Assert.That(writer.HasOutputFailed, Is.True);
            Assert.That(writer.OutputFilePath, Is.Null);
            Assert.That(File.Exists(filePath), Is.False);
        }

        [Test]
        public void Open_WhenOpeningTheStreamIsSlow_ReturnsWithoutWaitingForStorage()
        {
            string filePath = Path.Combine(_tempDirectory, "slow-open.fcrec");
            using var enteredOpen = new ManualResetEventSlim(false);
            using var releaseOpen = new ManualResetEventSlim(false);

            using var writer = new RecStreamWriter(
                filePath,
                segmentCapacity: 2,
                initialSegments: 2,
                axisFloatCapacityPerSegment: 8,
                streamFactory: _ =>
                {
                    enteredOpen.Set();
                    releaseOpen.Wait(TimeSpan.FromSeconds(10d));
                    return new MemoryStream();
                },
                postFinalizeAction: null);

            var stopwatch = Stopwatch.StartNew();
            writer.Open(RecBaselineState.Empty);
            stopwatch.Stop();

            Assert.That(enteredOpen.Wait(TimeSpan.FromSeconds(2d)), Is.True, "The writer thread never started opening the stream.");
            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(1000));
            Assert.That(writer.OutputFilePath, Is.Null);

            // オープン待ちの間に届いたイベントも取りこぼさずに書き出される。
            writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
            releaseOpen.Set();
            writer.Complete(0.1d, 1);

            Assert.That(writer.OutputFilePath, Is.EqualTo(filePath));
            Assert.That(writer.HasOutputFailed, Is.False);
        }

        [Test]
        public void Complete_WhenWriterThreadIsBlocked_ReturnsAfterTimeout()
        {
            string filePath = Path.Combine(_tempDirectory, "slow-finalize.fcrec");
            using var enteredBlockedWrite = new ManualResetEventSlim(false);
            using var releaseBlockedWrite = new ManualResetEventSlim(false);
            var stream = new BlockingStream(enteredBlockedWrite, releaseBlockedWrite);

            using var writer = new RecStreamWriter(
                filePath,
                segmentCapacity: 2,
                initialSegments: 2,
                axisFloatCapacityPerSegment: 8,
                streamFactory: _ => stream,
                postFinalizeAction: null);

            writer.Open(RecBaselineState.Empty);
            writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);

            Assert.That(enteredBlockedWrite.Wait(TimeSpan.FromSeconds(2d)), Is.True, "The writer thread never reached the blocked write.");

            LogAssert.Expect(LogType.Error, new Regex("timed out"));

            var stopwatch = Stopwatch.StartNew();
            writer.Complete(0.1d, 1);
            stopwatch.Stop();

            Assert.That(stopwatch.ElapsedMilliseconds, Is.LessThan(3000));

            releaseBlockedWrite.Set();
        }

        private sealed class StubClock : IRecClock
        {
            public double ElapsedSeconds { get; set; }

            public void Reset()
            {
                ElapsedSeconds = 0d;
            }
        }

        private sealed class NoopObservationBus : IFacialInputObservationBus
        {
            public bool HasObservers => false;
            public void Subscribe(IFacialInputObserver observer) { }
            public void Unsubscribe(IFacialInputObserver observer) { }
            public void OnTriggerOn(string sourceId, string expressionId) { }
            public void OnTriggerOff(string sourceId, string expressionId) { }
            public void PublishAnalogSample(string sourceId, ReadOnlySpan<float> axes) { }
            public void PublishValueProviderSample(string sourceId, in ValueProviderSample sample) { }
            public void OnExpressionActivated(string sourceId, string expressionId) { }
            public void OnExpressionDeactivated(string sourceId, string expressionId) { }
            public void OnLayerWeightSample(string layerName, float weight) { }
            public void OnInputSourceWeightSample(string layerName, string slotId, float weight) { }
        }

        [Test]
        public void Open_WritesLayerDefinitionsAndWeightBaselinesBeforeRuntimeEvents()
        {
            string filePath = Path.Combine(_tempDirectory, "weight-baseline.fcrec");
            var baseline = new RecBaselineState(
                Array.Empty<RecBaselineState.TriggerEntry>(),
                Array.Empty<RecBaselineState.AnalogEntry>(),
                Array.Empty<RecBaselineState.ValueProviderEntry>(),
                Array.Empty<string>(),
                new[] { new LayerWeightEntry("face", 0.5f) },
                new[] { new InputSourceWeightEntry("face", "input", 0.75f) });

            using var writer = new RecStreamWriter(filePath);
            writer.Open(baseline);
            writer.AppendEvent(RecEvent.CreateLayerWeightSample(0.1d, 0), new[] { 0.25f });
            writer.Complete(0.1d, 1);

            Assert.That(RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result), Is.True);
            Assert.That(result.Timeline.LayerIds, Is.EqualTo(new[] { "face" }));
            Assert.That(result.Timeline.Baseline.TryGetLayerWeight("face", out float layerWeight), Is.True);
            Assert.That(layerWeight, Is.EqualTo(0.5f));
            Assert.That(result.Timeline.Baseline.TryGetInputSourceWeight("face", "input", out float sourceWeight), Is.True);
            Assert.That(sourceWeight, Is.EqualTo(0.75f));
            Assert.That(result.Timeline.Events[0].Kind, Is.EqualTo(RecEventKind.LayerWeightSample));
        }

        private sealed class BlockingStream : MemoryStream
        {
            private readonly ManualResetEventSlim _enteredBlockedWrite;
            private readonly ManualResetEventSlim _releaseBlockedWrite;
            private int _writeCount;

            public BlockingStream(ManualResetEventSlim enteredBlockedWrite, ManualResetEventSlim releaseBlockedWrite)
            {
                _enteredBlockedWrite = enteredBlockedWrite;
                _releaseBlockedWrite = releaseBlockedWrite;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                int writeIndex = Interlocked.Increment(ref _writeCount);
                if (writeIndex == 2)
                {
                    _enteredBlockedWrite.Set();
                    _releaseBlockedWrite.Wait(TimeSpan.FromSeconds(10d));
                }

                base.Write(buffer, offset, count);
            }
        }
    }
}
