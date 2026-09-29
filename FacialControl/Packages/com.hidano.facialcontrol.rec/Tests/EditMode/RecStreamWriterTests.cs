using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
using Hidano.FacialControl.Rec.Adapters.Recording;
using Hidano.FacialControl.Rec.Domain.Models;
using Hidano.FacialControl.Rec.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    public class RecStreamWriterTests
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
            Assert.That(result.Timeline.SourceIds, Is.EqualTo(new[] { "input:trigger", "input:gaze" }));
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

            using (var writer = new RecStreamWriter(filePath))
            {
                writer.Open(RecBaselineState.Empty);
                writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
                writer.Complete(0.1d, 1);

                Assert.That(writer.OutputFilePath, Is.EqualTo(Path.Combine(_tempDirectory, "existing-2.fcrec")));
                Assert.That(RecFileReader.TryRead(writer.OutputFilePath, out _), Is.True);
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
