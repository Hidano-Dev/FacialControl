using System;
using System.IO;
using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// <see cref="ExpressionThumbnailDiskCache"/>。保存したものが読めること、削除できること、
    /// パスとして危険なキーを受け付けないことを守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class ExpressionThumbnailDiskCacheTests : SizedTestFixture
    {
        private const string Key = "0123456789abcdef";

        private string _directory;
        private ExpressionThumbnailDiskCache _cache;

        [SetUp]
        public void SetUp()
        {
            _directory = Path.Combine(Path.GetTempPath(), "FacialControlThumbnailCacheTests_" + Guid.NewGuid().ToString("N"));
            _cache = new ExpressionThumbnailDiskCache(_directory);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, true);
            }
        }

        [Test]
        public void TryLoad_NotSaved_ReturnsFalse()
        {
            Assert.That(_cache.TryLoad(Key, out var bytes), Is.False);
            Assert.That(bytes, Is.Null);
        }

        [Test]
        public void Save_ThenTryLoad_ReturnsSameBytes()
        {
            var data = new byte[] { 1, 2, 3, 4 };

            _cache.Save(Key, data);

            Assert.That(_cache.TryLoad(Key, out var loaded), Is.True);
            Assert.That(loaded, Is.EqualTo(data));
            Assert.That(File.Exists(_cache.GetPath(Key)), Is.True);
            Assert.That(File.Exists(_cache.GetPath(Key) + ".tmp"), Is.False, "一時ファイルが残っています。");
        }

        [Test]
        public void Save_ExistingKey_OverwritesBytes()
        {
            _cache.Save(Key, new byte[] { 1 });
            _cache.Save(Key, new byte[] { 9, 9 });

            Assert.That(_cache.TryLoad(Key, out var loaded), Is.True);
            Assert.That(loaded, Is.EqualTo(new byte[] { 9, 9 }));
        }

        [Test]
        public void Delete_SavedKey_RemovesFile()
        {
            _cache.Save(Key, new byte[] { 1 });

            _cache.Delete(Key);

            Assert.That(_cache.TryLoad(Key, out _), Is.False);
        }

        [Test]
        public void Delete_MissingKey_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _cache.Delete(Key));
        }

        [TestCase("../evil")]
        [TestCase("ABCDEF")]
        [TestCase("")]
        [TestCase(null)]
        public void Save_InvalidKey_WritesNothing(string key)
        {
            _cache.Save(key, new byte[] { 1 });

            Assert.That(ExpressionThumbnailDiskCache.IsValidKey(key), Is.False);
            Assert.That(Directory.Exists(_directory), Is.False, "不正なキーでディレクトリが作られました。");
        }
    }
}
