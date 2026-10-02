using System;
using System.IO;
using Hidano.FacialControl.Rec.Adapters.Recording;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Rec.Tests.EditMode
{
    /// <summary>
    /// 一時ディレクトリを前提にする <see cref="RecSidecarPath.ResolveUniqueFilePath"/> の fixture。
    /// ファイル I/O を使うため Medium。静的チェックはファイル単位で見るので Small の
    /// <c>RecSidecarPathTests</c> とは別ファイルにしている（docs/testing.md）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class RecSidecarPathResolveUniqueFilePathTests : SizedTestFixture
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
