using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class AdapterSlugTests
    {
        // ---------------------------------------------------------------
        // TryParse: valid characters / boundary length
        // ---------------------------------------------------------------

        [TestCase("osc")]
        [TestCase("lipsync")]
        [TestCase("input")]
        [TestCase("input-system")]
        [TestCase("analog-blendshape")]
        [TestCase("analog-bonepose")]
        [TestCase("legacy")]
        [TestCase("Alpha123")]
        [TestCase("a")]
        [TestCase("_under_score")]
        [TestCase("dot.separated")]
        [TestCase("dash-separated")]
        [TestCase("x-mycompany-arm-sensor")]
        public void TryParse_AllowedAsciiCharacters_IsAccepted(string candidate)
        {
            var parsed = AdapterSlug.TryParse(candidate, out var slug);

            Assert.IsTrue(parsed);
            Assert.AreEqual(candidate, slug.Value);
        }

        [Test]
        public void TryParse_ExactlySixtyFourCharacters_IsAccepted()
        {
            var boundary = new string('a', 64);

            var parsed = AdapterSlug.TryParse(boundary, out var slug);

            Assert.IsTrue(parsed);
            Assert.AreEqual(boundary, slug.Value);
        }

        [Test]
        public void TryParse_ExceedsSixtyFourCharacters_IsRejected()
        {
            var tooLong = new string('a', 65);

            var parsed = AdapterSlug.TryParse(tooLong, out var slug);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        [TestCase("bad id")]
        [TestCase("bad/id")]
        [TestCase("bad:id")]
        [TestCase("日本語")]
        [TestCase("has\tTab")]
        [TestCase("has\nNewline")]
        [TestCase("has space")]
        public void TryParse_InvalidCharacters_IsRejected(string invalid)
        {
            var parsed = AdapterSlug.TryParse(invalid, out var slug);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        [Test]
        public void TryParse_Null_IsRejected()
        {
            var parsed = AdapterSlug.TryParse(null, out var slug);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        [Test]
        public void TryParse_Empty_IsRejected()
        {
            var parsed = AdapterSlug.TryParse(string.Empty, out var slug);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        // ---------------------------------------------------------------
        // Parse: same rules as TryParse, exception path on invalid
        // ---------------------------------------------------------------

        [Test]
        public void Parse_ValidInput_ReturnsSlugWithSameValue()
        {
            var slug = AdapterSlug.Parse("osc");

            Assert.AreEqual("osc", slug.Value);
        }

        [Test]
        public void Parse_NullInput_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => AdapterSlug.Parse(null));
        }

        [Test]
        public void Parse_EmptyInput_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => AdapterSlug.Parse(string.Empty));
        }

        [Test]
        public void Parse_InvalidCharacters_ThrowsFormatException()
        {
            Assert.Throws<FormatException>(() => AdapterSlug.Parse("bad id"));
            Assert.Throws<FormatException>(() => AdapterSlug.Parse("bad:sub"));
            Assert.Throws<FormatException>(() => AdapterSlug.Parse("日本語"));
        }

        [Test]
        public void Parse_TooLong_ThrowsFormatException()
        {
            var tooLong = new string('a', 65);

            Assert.Throws<FormatException>(() => AdapterSlug.Parse(tooLong));
        }

        // ---------------------------------------------------------------
        // FromDisplayName: kebab-case auto-generation
        // ---------------------------------------------------------------

        [Test]
        public void FromDisplayName_UpperCaseAcronym_ReturnsLowerCase()
        {
            var slug = AdapterSlug.FromDisplayName("OSC");

            Assert.AreEqual("osc", slug.Value);
        }

        [Test]
        public void FromDisplayName_SpaceSeparated_ReturnsKebabCase()
        {
            var slug = AdapterSlug.FromDisplayName("Input System");

            Assert.AreEqual("input-system", slug.Value);
        }

        [Test]
        public void FromDisplayName_MultipleWords_ReturnsKebabCase()
        {
            var slug = AdapterSlug.FromDisplayName("ARKit / PerfectSync");

            // 空白・記号 → `-` 置換 + 連続 `-` 圧縮 + ToLowerInvariant
            Assert.AreEqual("arkit-perfectsync", slug.Value);
        }

        [Test]
        public void FromDisplayName_ConsecutiveSeparators_ArecompressedToSingleDash()
        {
            var slug = AdapterSlug.FromDisplayName("Mock   Trigger");

            Assert.AreEqual("mock-trigger", slug.Value);
        }

        [Test]
        public void FromDisplayName_Result_PassesTryParseRoundTrip()
        {
            var slug = AdapterSlug.FromDisplayName("Input System");

            Assert.IsTrue(AdapterSlug.TryParse(slug.Value, out var roundTrip));
            Assert.AreEqual(slug, roundTrip);
        }

        // ---------------------------------------------------------------
        // TryParseComposite: <slug> / <slug>:<sub>
        // ---------------------------------------------------------------

        [Test]
        public void TryParseComposite_SimpleSlug_ReturnsSlugAndEmptySub()
        {
            var parsed = AdapterSlug.TryParseComposite("osc", out var slug, out var sub);

            Assert.IsTrue(parsed);
            Assert.AreEqual("osc", slug.Value);
            Assert.IsTrue(string.IsNullOrEmpty(sub));
        }

        [Test]
        public void TryParseComposite_SlugWithSub_ReturnsBoth()
        {
            var parsed = AdapterSlug.TryParseComposite("osc:left-eye", out var slug, out var sub);

            Assert.IsTrue(parsed);
            Assert.AreEqual("osc", slug.Value);
            Assert.AreEqual("left-eye", sub);
        }

        [Test]
        public void TryParseComposite_InvalidSlugPart_IsRejected()
        {
            // slug 部分が不正文字を含むケース
            var parsed = AdapterSlug.TryParseComposite("bad slug:sub", out var slug, out var sub);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        [Test]
        public void TryParseComposite_Null_IsRejected()
        {
            var parsed = AdapterSlug.TryParseComposite(null, out var slug, out var sub);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        [Test]
        public void TryParseComposite_Empty_IsRejected()
        {
            var parsed = AdapterSlug.TryParseComposite(string.Empty, out var slug, out var sub);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        [Test]
        public void TryParseComposite_EmptySub_IsRejected()
        {
            // "<slug>:" のように sub が空のケースは不正とする
            var parsed = AdapterSlug.TryParseComposite("osc:", out var slug, out var sub);

            Assert.IsFalse(parsed);
            Assert.AreEqual(default(AdapterSlug), slug);
        }

        // ---------------------------------------------------------------
        // Equality / GetHashCode / operator==/!=
        // ---------------------------------------------------------------

        [Test]
        public void Equality_SameValue_AreEqual()
        {
            Assert.IsTrue(AdapterSlug.TryParse("osc", out var a));
            Assert.IsTrue(AdapterSlug.TryParse("osc", out var b));

            Assert.AreEqual(a, b);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentValue_AreNotEqual()
        {
            Assert.IsTrue(AdapterSlug.TryParse("osc", out var a));
            Assert.IsTrue(AdapterSlug.TryParse("input-system", out var b));

            Assert.AreNotEqual(a, b);
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equality_AgainstObject_HandlesNonSlugType()
        {
            Assert.IsTrue(AdapterSlug.TryParse("osc", out var a));

            Assert.IsFalse(a.Equals("osc"));
            Assert.IsFalse(a.Equals(null));
        }

        [Test]
        public void Equality_DefaultInstances_AreEqual()
        {
            var a = default(AdapterSlug);
            var b = default(AdapterSlug);

            Assert.IsTrue(a == b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        // ---------------------------------------------------------------
        // ToString
        // ---------------------------------------------------------------

        [Test]
        public void ToString_ReturnsUnderlyingValue()
        {
            Assert.IsTrue(AdapterSlug.TryParse("input-system", out var slug));

            Assert.AreEqual("input-system", slug.ToString());
        }

        [Test]
        public void ToString_DefaultInstance_ReturnsEmptyString()
        {
            var defaultSlug = default(AdapterSlug);

            Assert.AreEqual(string.Empty, defaultSlug.ToString());
        }

        // ---------------------------------------------------------------
        // Default state
        // ---------------------------------------------------------------

        [Test]
        public void Default_ValueIsNull()
        {
            var defaultSlug = default(AdapterSlug);

            Assert.IsNull(defaultSlug.Value);
        }
    }
}
