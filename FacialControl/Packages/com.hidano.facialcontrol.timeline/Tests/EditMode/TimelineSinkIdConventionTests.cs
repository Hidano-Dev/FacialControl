using System;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Domain.Services;
using NUnit.Framework;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    [SmallTest]
    public sealed class TimelineSinkIdConventionTests : SizedTestFixture
    {
        private static readonly AdapterSlug TimelineSlug = AdapterSlug.Parse("timeline");

        [Test]
        public void ComposeValueId_AsciiLayerName_UsesLayerName()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "emotion", 2, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:emotion"));
            Assert.That(usedIndexFallback, Is.False);
        }

        [Test]
        public void ComposeValueId_AllowedSymbols_UsesLayerName()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "Eye_L.base-1", 0, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:Eye_L.base-1"));
            Assert.That(usedIndexFallback, Is.False);
        }

        [Test]
        public void ComposeValueId_NonAsciiLayerName_FallsBackToLayerIndex()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "感情", 1, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:layer1"));
            Assert.That(usedIndexFallback, Is.True);
        }

        [Test]
        public void ComposeValueId_DistinctNonAsciiLayerNames_DoNotCollide()
        {
            InputSourceId first = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "感情", 0, out _);
            InputSourceId second = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "表情", 1, out _);

            Assert.That(first, Is.Not.EqualTo(second));
        }

        [Test]
        public void ComposeValueId_LayerNameWithColon_FallsBackToLayerIndex()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "emo:tion", 3, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:layer3"));
            Assert.That(usedIndexFallback, Is.True);
        }

        [Test]
        public void ComposeValueId_LayerNameWithSpace_FallsBackToLayerIndex()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "left eye", 0, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:layer0"));
            Assert.That(usedIndexFallback, Is.True);
        }

        [Test]
        public void ComposeValueId_EmptyLayerName_FallsBackToLayerIndex()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, string.Empty, 4, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:layer4"));
            Assert.That(usedIndexFallback, Is.True);
        }

        [Test]
        public void ComposeValueId_StateFormExactly64Chars_UsesLayerName()
        {
            // "timeline:" (9) + name + ":state" (6) = 64 → name は 49 文字
            string name = new string('a', 49);

            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, name, 0, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:" + name));
            Assert.That(usedIndexFallback, Is.False);
        }

        [Test]
        public void ComposeValueId_StateFormOver64Chars_FallsBackToLayerIndex()
        {
            string name = new string('a', 50);

            InputSourceId id = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, name, 5, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:layer5"));
            Assert.That(usedIndexFallback, Is.True);
        }

        [Test]
        public void ComposeStateId_AsciiLayerName_AppendsStateSuffix()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeStateId(TimelineSlug, "emotion", 0, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:emotion:state"));
            Assert.That(usedIndexFallback, Is.False);
        }

        [Test]
        public void ComposeStateId_NonAsciiLayerName_UsesIndexFormWithStateSuffix()
        {
            InputSourceId id = TimelineSinkIdConvention.ComposeStateId(TimelineSlug, "感情", 2, out bool usedIndexFallback);

            Assert.That(id.Value, Is.EqualTo("timeline:layer2:state"));
            Assert.That(usedIndexFallback, Is.True);
        }

        [Test]
        public void ComposeStateId_StateFormExactly64Chars_UsesLayerName()
        {
            string name = new string('b', 49);

            InputSourceId id = TimelineSinkIdConvention.ComposeStateId(TimelineSlug, name, 0, out bool usedIndexFallback);

            Assert.That(id.Value.Length, Is.EqualTo(64));
            Assert.That(usedIndexFallback, Is.False);
        }

        [Test]
        public void ComposeValueId_SameInput_ReturnsSameId()
        {
            InputSourceId first = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "感情", 1, out bool firstFallback);
            InputSourceId second = TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "感情", 1, out bool secondFallback);

            Assert.That(first, Is.EqualTo(second));
            Assert.That(firstFallback, Is.EqualTo(secondFallback));
        }

        [Test]
        public void ComposeValueId_NegativeLayerIndexWithFallback_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => TimelineSinkIdConvention.ComposeValueId(TimelineSlug, "感情", -1, out _));
        }

        [Test]
        public void ComposeValueId_DefaultSlug_Throws()
        {
            Assert.Throws<ArgumentException>(
                () => TimelineSinkIdConvention.ComposeValueId(default, "emotion", 0, out _));
        }

        [Test]
        public void IsNameAddressable_AsciiName_ReturnsTrue()
        {
            Assert.That(TimelineSinkIdConvention.IsNameAddressable("emotion"), Is.True);
        }

        [Test]
        public void IsNameAddressable_NonAsciiOrColonOrNull_ReturnsFalse()
        {
            Assert.That(TimelineSinkIdConvention.IsNameAddressable("感情"), Is.False);
            Assert.That(TimelineSinkIdConvention.IsNameAddressable("a:b"), Is.False);
            Assert.That(TimelineSinkIdConvention.IsNameAddressable(null), Is.False);
            Assert.That(TimelineSinkIdConvention.IsNameAddressable(new string('a', 50)), Is.False);
        }

        [Test]
        public void IsLegacyStateDeclaration_NameForm_ReturnsTrue()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("timeline:emotion:state", TimelineSlug), Is.True);
        }

        [Test]
        public void IsLegacyStateDeclaration_IndexForm_ReturnsTrue()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("timeline:layer0:state", TimelineSlug), Is.True);
        }

        [Test]
        public void IsLegacyStateDeclaration_ValueSinkDeclaration_ReturnsFalse()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("timeline:emotion", TimelineSlug), Is.False);
        }

        [Test]
        public void IsLegacyStateDeclaration_OtherSlug_ReturnsFalse()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("osc:emotion:state", TimelineSlug), Is.False);
        }

        [Test]
        public void IsLegacyStateDeclaration_SlugPrefixOnlyPartiallyMatches_ReturnsFalse()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("timeline2:emotion:state", TimelineSlug), Is.False);
        }

        [Test]
        public void IsLegacyStateDeclaration_EmptyMiddleSegment_ReturnsFalse()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("timeline::state", TimelineSlug), Is.False);
        }

        [Test]
        public void IsLegacyStateDeclaration_NotParsableAsInputSourceId_ReturnsFalse()
        {
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration("timeline:感情:state", TimelineSlug), Is.False);
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration(null, TimelineSlug), Is.False);
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration(string.Empty, TimelineSlug), Is.False);
        }

        [Test]
        public void IsLegacyStateDeclaration_ComposedStateId_ReturnsTrue()
        {
            InputSourceId nameForm = TimelineSinkIdConvention.ComposeStateId(TimelineSlug, "emotion", 0, out _);
            InputSourceId indexForm = TimelineSinkIdConvention.ComposeStateId(TimelineSlug, "感情", 1, out _);

            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration(nameForm.Value, TimelineSlug), Is.True);
            Assert.That(TimelineSinkIdConvention.IsLegacyStateDeclaration(indexForm.Value, TimelineSlug), Is.True);
        }
    }
}
