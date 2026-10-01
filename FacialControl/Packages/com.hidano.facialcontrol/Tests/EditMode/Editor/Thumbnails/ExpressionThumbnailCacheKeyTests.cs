using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Thumbnails;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Thumbnails
{
    /// <summary>
    /// <see cref="ExpressionThumbnailCacheKey"/>。
    /// 同じ入力なら同じキー（キャッシュが効く）、見た目に関わる入力が変われば別のキー（再生成される）を守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class ExpressionThumbnailCacheKeyTests : SizedTestFixture
    {
        private const string ModelId = "model-guid:1:hash";
        private const string ClipId = "clip-guid:2:hash";
        private const int Resolution = 512;

        [Test]
        public void Compute_SameInputs_ReturnsSameKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), Resolution);

            Assert.That(a, Is.EqualTo(b));
        }

        [Test]
        public void Compute_Always_ReturnsSixteenLowercaseHexChars()
        {
            var key = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), Resolution);

            Assert.That(key, Does.Match("^[0-9a-f]{16}$"));
            Assert.That(ExpressionThumbnailDiskCache.IsValidKey(key), Is.True);
        }

        [Test]
        public void Compute_KnownInput_IsStableAcrossProcesses()
        {
            // string.GetHashCode のようなプロセスごとに変わるハッシュを使っていないことを固定値で確認する。
            // 生成方式を変えて FormatVersion を上げたときは、この期待値も更新する。
            var key = ExpressionThumbnailCacheKey.Compute(string.Empty, string.Empty, ExpressionSnapshot.CreateDefault("x"), 0);

            Assert.That(key, Is.EqualTo(ExpressionThumbnailCacheKey.Compute(null, null, ExpressionSnapshot.CreateDefault("y"), 0)),
                "snapshot の Id はキーに含めない（Expression 名を変えただけでは再生成しない）。");
            Assert.That(key, Is.EqualTo("5b2a969b42d238a4"));
        }

        [Test]
        public void Compute_BlendShapeValueChanged_ReturnsDifferentKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.6f), Resolution);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Compute_ModelChanged_ReturnsDifferentKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute("other-model:1:hash", ClipId, CreateSnapshot(0.5f), Resolution);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Compute_ClipIdentityChanged_ReturnsDifferentKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute(ModelId, "clip-guid:2:newhash", CreateSnapshot(0.5f), Resolution);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Compute_ResolutionChanged_ReturnsDifferentKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), 512);
            var b = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0.5f), 256);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Compute_BoneRotationChanged_ReturnsDifferentKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshotWithBone(10f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshotWithBone(12f), Resolution);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Compute_StringBoundaryShifted_ReturnsDifferentKey()
        {
            // ("ab","c") と ("a","bc") を連結すると同じ文字列になるが、別のキーになること。
            var a = ExpressionThumbnailCacheKey.Compute("ab", "c", CreateSnapshot(0.5f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute("a", "bc", CreateSnapshot(0.5f), Resolution);

            Assert.That(a, Is.Not.EqualTo(b));
        }

        [Test]
        public void Compute_NegativeZeroAndPositiveZero_ReturnsSameKey()
        {
            var a = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(0f), Resolution);
            var b = ExpressionThumbnailCacheKey.Compute(ModelId, ClipId, CreateSnapshot(-0f), Resolution);

            Assert.That(a, Is.EqualTo(b));
        }

        private static ExpressionSnapshot CreateSnapshot(float smile)
        {
            return new ExpressionSnapshot(
                "expr",
                0.1f,
                TransitionCurvePreset.Linear,
                new[] { new BlendShapeSnapshot("Body", "smile", smile), new BlendShapeSnapshot("Body", "blink", 1f) },
                new BoneSnapshot[0],
                new[] { "Body" });
        }

        private static ExpressionSnapshot CreateSnapshotWithBone(float eulerX)
        {
            return new ExpressionSnapshot(
                "expr",
                0.1f,
                TransitionCurvePreset.Linear,
                new BlendShapeSnapshot[0],
                new[] { new BoneSnapshot("Armature/Head/Jaw", 0f, 0f, 0f, eulerX, 0f, 0f, 1f, 1f, 1f) },
                new string[0]);
        }
    }
}
