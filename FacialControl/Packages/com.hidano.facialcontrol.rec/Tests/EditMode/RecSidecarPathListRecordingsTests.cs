using System;
using System.Collections.Generic;
using System.IO;
using Hidano.FacialControl.Rec.Adapters.Recording;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    /// <summary>
    /// 一時ディレクトリに録画ファイルを置いて <see cref="RecSidecarPath.ListRecordings"/> の列挙を検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class RecSidecarPathListRecordingsTests : SizedTestFixture
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FacialControlRecListRecordingsTests", Guid.NewGuid().ToString("N"));
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
        public void ListRecordings_DirectoryDoesNotExist_ReturnsEmpty()
        {
            string missingDirectory = Path.Combine(_tempDirectory, "missing");

            IReadOnlyList<RecRecordingEntry> recordings = RecSidecarPath.ListRecordings(missingDirectory);

            Assert.That(recordings, Is.Empty);
        }

        [Test]
        public void ListRecordings_MixedFiles_ReturnsOnlyRecordingFilesWithNameWithoutExtension()
        {
            string takePath = CreateFile("take01" + RecSidecarPath.FileExtension, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
            CreateFile("notes.txt", new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));
            CreateFile("take01" + RecSidecarPath.FileExtension + ".meta", new DateTime(2026, 9, 3, 0, 0, 0, DateTimeKind.Utc));

            IReadOnlyList<RecRecordingEntry> recordings = RecSidecarPath.ListRecordings(_tempDirectory);

            Assert.That(recordings.Count, Is.EqualTo(1));
            Assert.That(recordings[0].Name, Is.EqualTo("take01"));
            Assert.That(recordings[0].FilePath, Is.EqualTo(takePath));
            Assert.That(recordings[0].LastWriteTimeUtc, Is.EqualTo(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Test]
        public void ListRecordings_MultipleRecordings_SortsNewestFirstThenByName()
        {
            var older = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            var newer = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc);
            CreateFile("b-old" + RecSidecarPath.FileExtension, older);
            CreateFile("z-new" + RecSidecarPath.FileExtension, newer);
            CreateFile("a-new" + RecSidecarPath.FileExtension, newer);

            IReadOnlyList<RecRecordingEntry> recordings = RecSidecarPath.ListRecordings(_tempDirectory);

            Assert.That(recordings.Count, Is.EqualTo(3));
            Assert.That(recordings[0].Name, Is.EqualTo("a-new"));
            Assert.That(recordings[1].Name, Is.EqualTo("z-new"));
            Assert.That(recordings[2].Name, Is.EqualTo("b-old"));
        }

        [Test]
        public void ListRecordings_ExcludedFilePathGiven_OmitsThatRecording()
        {
            CreateFile("take01" + RecSidecarPath.FileExtension, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
            string recordingInProgress = CreateFile("take02" + RecSidecarPath.FileExtension, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));

            IReadOnlyList<RecRecordingEntry> recordings = RecSidecarPath.ListRecordings(_tempDirectory, recordingInProgress);

            Assert.That(recordings.Count, Is.EqualTo(1));
            Assert.That(recordings[0].Name, Is.EqualTo("take01"));
        }

        [Test]
        public void ListRecordings_NameThatCannotBeLoadedAsIs_IsOmitted()
        {
            CreateFile("take01" + RecSidecarPath.FileExtension, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
            CreateFile("v1..2" + RecSidecarPath.FileExtension, new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc));

            IReadOnlyList<RecRecordingEntry> recordings = RecSidecarPath.ListRecordings(_tempDirectory);

            Assert.That(recordings.Count, Is.EqualTo(1));
            Assert.That(recordings[0].Name, Is.EqualTo("take01"));
        }

        [Test]
        public void ListRecordings_SubdirectoryWithRecordingExtension_IsIgnored()
        {
            Directory.CreateDirectory(Path.Combine(_tempDirectory, "folder" + RecSidecarPath.FileExtension));
            Directory.CreateDirectory(Path.Combine(_tempDirectory, "nested"));
            CreateFile(Path.Combine("nested", "deep" + RecSidecarPath.FileExtension), new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

            IReadOnlyList<RecRecordingEntry> recordings = RecSidecarPath.ListRecordings(_tempDirectory);

            Assert.That(recordings, Is.Empty);
        }

        private string CreateFile(string relativePath, DateTime lastWriteTimeUtc)
        {
            string path = Path.Combine(_tempDirectory, relativePath);
            File.WriteAllBytes(path, new byte[] { 0x46, 0x52, 0x45, 0x43 });
            File.SetLastWriteTimeUtc(path, lastWriteTimeUtc);
            return path;
        }
    }
}
