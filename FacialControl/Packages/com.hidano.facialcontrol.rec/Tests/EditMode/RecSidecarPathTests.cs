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

        [Test]
        public void TryBuildRecordingFilePath_BackslashInName_RejectedOnEveryPlatform()
        {
            // '\' は Linux / macOS では区切り文字でも無効文字でもないが、Windows と同じく区切り文字として扱い
            // 拒否する（OS 依存の Path.DirectorySeparatorChar / GetInvalidFileNameChars には頼らない）。
            // 同じ名前が OS によって別のパスに解決されることを防ぐ
            bool success = RecSidecarPath.TryBuildRecordingFilePath("Miku", "take\\A", out _, out string error);

            Assert.That(success, Is.False);
            Assert.That(error, Does.Contain("recordingName"));
        }
    }
}
