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
        // 16 桁の 16 進小文字（キャッシュキーの形式）。
        private const string Key = "000000000000000a";

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

        [Test]
        public void Prune_OverLimit_DeletesLeastRecentlyUsedFiles()
        {
            const string oldKey = "0000000000000001";
            const string usedKey = "0000000000000002";
            const string newKey = "0000000000000003";
            _cache.Save(oldKey, new byte[] { 1 });
            _cache.Save(usedKey, new byte[] { 2 });
            _cache.Save(newKey, new byte[] { 3 });
            var baseTime = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(_cache.GetPath(oldKey), baseTime);
            File.SetLastWriteTimeUtc(_cache.GetPath(usedKey), baseTime.AddMinutes(1));
            File.SetLastWriteTimeUtc(_cache.GetPath(newKey), baseTime.AddMinutes(2));

            // 読み込んだキャッシュは最近使ったものとして残る。
            Assert.That(_cache.TryLoad(usedKey, out _), Is.True);
            int deleted = _cache.Prune(2);

            Assert.That(deleted, Is.EqualTo(1));
            Assert.That(File.Exists(_cache.GetPath(oldKey)), Is.False);
            Assert.That(File.Exists(_cache.GetPath(usedKey)), Is.True);
            Assert.That(File.Exists(_cache.GetPath(newKey)), Is.True);
        }

        [Test]
        public void Prune_MissingDirectory_ReturnsZero()
        {
            Assert.That(_cache.Prune(0), Is.Zero);
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
