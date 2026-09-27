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
        public void Open_WhenFileAlreadyExists_LeavesExistingFileUntouched()
        {
            string filePath = Path.Combine(_tempDirectory, "existing.fcrec");
            byte[] original = { 1, 2, 3, 4 };
            File.WriteAllBytes(filePath, original);

            LogAssert.Expect(LogType.Error, new Regex("REC writer I/O failed"));

            using (var writer = new RecStreamWriter(filePath))
            {
                writer.Open(RecBaselineState.Empty);
                writer.AppendEvent(RecEvent.CreateTriggerOn(0.1d, 0, 0), ReadOnlySpan<float>.Empty);
                writer.Complete(0.1d, 1);
            }

            Assert.That(File.ReadAllBytes(filePath), Is.EqualTo(original));
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
