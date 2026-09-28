using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="InputSourceId"/> の規約テスト。
    /// </summary>
    /// <remarks>
    /// 旧 reserved id 体系（<c>osc</c> / <c>lipsync</c> / <c>input</c> 等）は D-13 で
    /// 廃止済みのため、識別子の意味付けは <see cref="AdapterSlug"/> 側のテストでカバーされる。
    /// 本ファイルは識別子文字列としての validation 契約（regex / 長さ / legacy 拒否 / 等価性）のみを保持する。
    /// </remarks>
    [TestFixture]
    public class InputSourceIdTests
    {
        [TestCase("x-mycompany-arm-sensor")]
        [TestCase("x-test")]
        [TestCase("x-")]
        public void TryParse_ThirdPartyPrefix_ReturnsTrueAndIsThirdPartyExtensionIsTrue(string extension)
        {
            var parsed = InputSourceId.TryParse(extension, out var id);

            Assert.IsTrue(parsed);
            Assert.AreEqual(extension, id.Value);
            Assert.IsTrue(id.IsThirdPartyExtension);
        }

        [Test]
        public void TryParse_LegacyIdentifier_IsRejected()
        {
            var parsed = InputSourceId.TryParse("legacy", out var id);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(InputSourceId), id);
        }

        [Test]
        public void TryParse_Null_IsRejected()
        {
            var parsed = InputSourceId.TryParse(null, out var id);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(InputSourceId), id);
        }

        [Test]
        public void TryParse_Empty_IsRejected()
        {
            var parsed = InputSourceId.TryParse(string.Empty, out var id);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(InputSourceId), id);
        }

        [TestCase("bad id")]
        [TestCase("bad/id")]
        [TestCase("日本語")]
        [TestCase("has\tTab")]
        [TestCase("has\nNewline")]
        public void TryParse_InvalidCharacters_IsRejected(string invalid)
        {
            var parsed = InputSourceId.TryParse(invalid, out var id);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(InputSourceId), id);
        }

        [Test]
        public void TryParse_ExceedsSixtyFourCharacters_IsRejected()
        {
            var tooLong = new string('a', 65);

            var parsed = InputSourceId.TryParse(tooLong, out var id);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(InputSourceId), id);
        }

        [Test]
        public void TryParse_ExactlySixtyFourCharacters_IsAccepted()
        {
            var boundary = new string('a', 64);

            var parsed = InputSourceId.TryParse(boundary, out var id);

            Assert.IsTrue(parsed);
            Assert.AreEqual(boundary, id.Value);
        }

        [TestCase("_under_score")]
        [TestCase("dot.separated")]
        [TestCase("dash-separated")]
        [TestCase("Alpha123")]
        [TestCase("a")]
        [TestCase("slug:sub")]
        [TestCase("input:analog-expression")]
        [TestCase("x-vendor:event")]
        public void TryParse_AllowedAsciiCharacters_IsAccepted(string candidate)
        {
            var parsed = InputSourceId.TryParse(candidate, out var id);

            Assert.IsTrue(parsed);
            Assert.AreEqual(candidate, id.Value);
        }

        [Test]
        public void Equality_SameValue_AreEqual()
        {
            Assert.IsTrue(InputSourceId.TryParse("alpha", out var a));
            Assert.IsTrue(InputSourceId.TryParse("alpha", out var b));

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentValue_AreNotEqual()
        {
            Assert.IsTrue(InputSourceId.TryParse("alpha", out var a));
            Assert.IsTrue(InputSourceId.TryParse("beta", out var b));

            Assert.AreNotEqual(a, b);
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void ToString_ReturnsUnderlyingValue()
        {
            Assert.IsTrue(InputSourceId.TryParse("input", out var id));

            Assert.AreEqual("input", id.ToString());
        }

        [Test]
        public void Default_ValueIsNullAndIsThirdPartyExtensionIsFalse()
        {
            var defaultId = default(InputSourceId);

            Assert.IsNull(defaultId.Value);
            Assert.IsFalse(defaultId.IsThirdPartyExtension);
        }
    }
}
