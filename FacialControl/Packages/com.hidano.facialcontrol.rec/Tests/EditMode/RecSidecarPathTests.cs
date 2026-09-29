using System;
using System.IO;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Rec.Adapters.Recording;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    [TestFixture]
    [SmallTest]
    public class RecSidecarPathTests : SizedTestFixture
    {
        [Test]
        public void TryBuildRecordingFilePath_ValidNames_ReturnsExpectedPath()
        {
            bool success = RecSidecarPath.TryBuildRecordingFilePath("Miku", "session_01", out string path, out string error);

            Assert.That(success, Is.True, error);
            Assert.That(path, Is.Not.Null);
            string expectedTail = Path.Combine(
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                "Miku",
                RecSidecarPath.RecordingsFolderName,
                "session_01" + RecSidecarPath.FileExtension);
            StringAssert.EndsWith(expectedTail, path);
        }

        [Test]
        public void TryBuildRecordingFilePath_InvalidFileNameChars_SanitizesSegments()
        {
            bool success = RecSidecarPath.TryBuildRecordingFilePath("Miku:01*", "take?A", out string path, out string error);

            Assert.That(success, Is.True, error);
            string expectedTail = Path.Combine(
                FacialCharacterProfileSO.StreamingAssetsRootFolder,
                "Miku-01-",
                RecSidecarPath.RecordingsFolderName,
                "take-A" + RecSidecarPath.FileExtension);
            StringAssert.EndsWith(expectedTail, path);
        }

        [Test]
        public void TryBuildRecordingFilePath_TraversalName_ReturnsFalse()
        {
            Assert.That(
                RecSidecarPath.TryBuildRecordingFilePath("../Miku", "session", out _, out string assetError),
                Is.False);
            Assert.That(assetError, Does.Contain("assetName"));

            Assert.That(
                RecSidecarPath.TryBuildRecordingFilePath("Miku", "..\\session", out _, out string recordingError),
                Is.False);
            Assert.That(recordingError, Does.Contain("recordingName"));
        }
    }

    /// <summary>
    /// 一時ディレクトリを前提にする <see cref="RecSidecarPath.ResolveUniqueFilePath"/> の fixture。
    /// </summary>
    [TestFixture]
    public class RecSidecarPathResolveUniqueFilePathTests
    {
        private string _tempDirectory;

        [SetUp]
        public void SetUp()
        {
            _tempDirectory = Path.Combine(Path.GetTempPath(), "FacialControlRecSidecarPathTests", Guid.NewGuid().ToString("N"));
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
        public void ResolveUniqueFilePath_FileDoesNotExist_ReturnsSamePath()
        {
            string filePath = Path.Combine(_tempDirectory, "take01" + RecSidecarPath.FileExtension);

            string resolved = RecSidecarPath.ResolveUniqueFilePath(filePath);

            Assert.That(resolved, Is.EqualTo(filePath));
        }

        [Test]
        public void ResolveUniqueFilePath_FileExists_AppendsSequenceSuffixInsteadOfOverwriting()
        {
            string filePath = Path.Combine(_tempDirectory, "take01" + RecSidecarPath.FileExtension);
            File.WriteAllText(filePath, "original");

            string resolved = RecSidecarPath.ResolveUniqueFilePath(filePath);

            Assert.That(resolved, Is.EqualTo(Path.Combine(_tempDirectory, "take01-2" + RecSidecarPath.FileExtension)));
            Assert.That(File.ReadAllText(filePath), Is.EqualTo("original"));
        }

        [Test]
        public void ResolveUniqueFilePath_SuffixedFileAlsoExists_IncrementsUntilFree()
        {
            string filePath = Path.Combine(_tempDirectory, "take01" + RecSidecarPath.FileExtension);
            File.WriteAllText(filePath, "1");
            File.WriteAllText(Path.Combine(_tempDirectory, "take01-2" + RecSidecarPath.FileExtension), "2");

            string resolved = RecSidecarPath.ResolveUniqueFilePath(filePath);

            Assert.That(resolved, Is.EqualTo(Path.Combine(_tempDirectory, "take01-3" + RecSidecarPath.FileExtension)));
        }
    }
}
