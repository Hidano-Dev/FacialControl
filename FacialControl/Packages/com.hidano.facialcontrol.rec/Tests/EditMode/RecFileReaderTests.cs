using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Rec.Adapters.FileSystem;
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
    public class RecFileReaderTests : SizedTestFixture
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FacialControlRecTests", Guid.NewGuid().ToString("N"));
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
        public void TryRead_ValidFile_ReturnsTimeline()
        {
            RecTimeline expected = CreateTimeline();
            string filePath = WriteRecordingFile(RecBinaryFormat.Serialize(expected, 123L));

            bool success = RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result);

            Assert.That(success, Is.True);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasFooter, Is.True);
            Assert.That(result.RecoveredFromTruncatedTail, Is.False);
            Assert.That(result.Timeline.SourceIds, Is.EqualTo(expected.SourceIds));
            Assert.That(result.Timeline.ExpressionIds, Is.EqualTo(expected.ExpressionIds));
            Assert.That(result.Timeline.Events, Is.EqualTo(expected.Events));
            Assert.That(result.Timeline.GetAnalogAxes(1), Is.EqualTo(expected.GetAnalogAxes(1)));
        }

        [Test]
        public void TryRead_TruncatedFile_LogsWarningAndRecoversTimeline()
        {
            RecTimeline expected = CreateTimeline();
            byte[] bytes = RecBinaryFormat.Serialize(expected, 123L);
            Array.Resize(ref bytes, bytes.Length - 2);
            string filePath = WriteRecordingFile(bytes);

            LogAssert.Expect(LogType.Warning, new Regex("truncated"));

            bool success = RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result);

            Assert.That(success, Is.True);
            Assert.That(result, Is.Not.Null);
            Assert.That(result.HasFooter, Is.False);
            Assert.That(result.RecoveredFromTruncatedTail, Is.True);
            Assert.That(result.Timeline.Events, Is.EqualTo(expected.Events));
        }

        [Test]
        public void TryRead_MissingFile_LogsErrorAndReturnsFalse()
        {
            string filePath = Path.Combine(_tempDirectory, "missing.fcrec");

            LogAssert.Expect(LogType.Error, new Regex("REC load failed"));

            bool success = RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryRead_UnsupportedVersion_LogsErrorAndReturnsFalse()
        {
            byte[] bytes = RecBinaryFormat.Serialize(CreateTimeline(), 123L);
            bytes[4] = 2;
            bytes[5] = 0;
            string filePath = WriteRecordingFile(bytes);

            LogAssert.Expect(LogType.Error, new Regex("REC load failed.*version"));

            bool success = RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.Null);
        }

        [Test]
        public void TryRead_FileWithoutFullInputBaselineFlag_LogsErrorAndReturnsFalse()
        {
            byte[] bytes = RecBinaryFormat.Serialize(CreateTimeline(), 123L);
            bytes[6] = 0;
            bytes[7] = 0;
            string filePath = WriteRecordingFile(bytes);

            LogAssert.Expect(LogType.Error, new Regex("FullInputBaseline"));

            bool success = RecFileReader.TryRead(filePath, out RecBinaryFormat.ReadResult result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.Null);
        }

        private string WriteRecordingFile(byte[] bytes)
        {
            string filePath = Path.Combine(_tempDirectory, "test.fcrec");
            File.WriteAllBytes(filePath, bytes);
            return filePath;
        }

        private static RecTimeline CreateTimeline()
        {
            var baseline = new RecBaselineState(
                new[]
                {
                    new RecBaselineState.TriggerEntry("input:trigger", new[] { "smile" }),
                },
                new[]
                {
                    new RecBaselineState.AnalogEntry("input:gaze", new[] { 0.25f, -0.75f }),
                });

            RecEvent[] events =
            {
                RecEvent.CreateTriggerOn(0.1d, 0, 0),
                RecEvent.CreateAnalogSample(0.25d, 1, 2),
            };

            return new RecTimeline(
                baseline,
                events,
                new[] { "input:trigger", "input:gaze" },
                new[] { "smile" },
                0.25d,
                new IReadOnlyList<float>[]
                {
                    Array.Empty<float>(),
                    new[] { -0.1f, 0.2f },
                });
        }
    }
}
