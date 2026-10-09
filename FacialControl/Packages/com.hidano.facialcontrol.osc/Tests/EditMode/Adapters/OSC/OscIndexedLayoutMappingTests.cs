using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Testing;
using NUnit.Framework;

namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.OSC
{
    [SmallTest]
    public sealed class OscIndexedLayoutMappingTests : SizedTestFixture
    {
        [Test]
        public void Create_NamesInMesh_MapsSlotsToBufferIndicesInSlotOrder()
        {
            var layout = new OscFrameLayout(5, new[] { "笑い", "unknown", "blink" });

            OscIndexedLayoutMapping mapping = OscIndexedLayoutMapping.Create(layout, new[] { "blink", "笑い", "other" });

            Assert.That(mapping.Version, Is.EqualTo(5));
            Assert.That(mapping.BlendShapeSlotCount, Is.EqualTo(3));
            Assert.That(mapping.MatchedBlendShapeCount, Is.EqualTo(2));
            Assert.That(mapping.GetMappingIndex(0), Is.EqualTo(0));
            Assert.That(mapping.GetMappingIndex(1), Is.EqualTo(-1), "受信側に無い名前の slot は捨てる");
            Assert.That(mapping.GetMappingIndex(2), Is.EqualTo(1));
            Assert.That(mapping.RuntimeMappings[0].BlendShapeName, Is.EqualTo("笑い"));
            Assert.That(mapping.RuntimeMappings[1].BlendShapeName, Is.EqualTo("blink"));
            Assert.That(mapping.RuntimeMappings[0].OscAddress, Is.Empty);
        }

        [Test]
        public void GetMappingIndex_SlotOutOfRange_ReturnsMinusOne()
        {
            OscIndexedLayoutMapping mapping = OscIndexedLayoutMapping.Create(new OscFrameLayout(5, new[] { "a" }), new[] { "a" });

            Assert.That(mapping.GetMappingIndex(-1), Is.EqualTo(-1));
            Assert.That(mapping.GetMappingIndex(1), Is.EqualTo(-1));
        }

        [Test]
        public void Create_NameDiffersOnlyByCase_IsNotMatched()
        {
            var layout = new OscFrameLayout(5, new[] { "Blink" });

            OscIndexedLayoutMapping mapping = OscIndexedLayoutMapping.Create(layout, new[] { "blink" });

            Assert.That(mapping.GetMappingIndex(0), Is.EqualTo(-1));
            Assert.That(mapping.RuntimeMappings, Is.Empty);
        }

        [Test]
        public void Create_DuplicateNameInLayout_GivesEachSlotItsOwnIndex()
        {
            var layout = new OscFrameLayout(5, new[] { "a", "a" });

            OscIndexedLayoutMapping mapping = OscIndexedLayoutMapping.Create(layout, new[] { "a" });

            Assert.That(mapping.GetMappingIndex(0), Is.EqualTo(0));
            Assert.That(mapping.GetMappingIndex(1), Is.EqualTo(1));
            Assert.That(mapping.RuntimeMappings.Length, Is.EqualTo(2));
        }

        [Test]
        public void Create_GazeOnlyLayout_HasNoBlendShapeSlots()
        {
            var layout = new OscFrameLayout(5, null, new[] { new OscFrameLayoutGazeChannel("eye") });

            OscIndexedLayoutMapping mapping = OscIndexedLayoutMapping.Create(layout, new[] { "a" });

            Assert.That(mapping.BlendShapeSlotCount, Is.EqualTo(0));
            Assert.That(mapping.RuntimeMappings, Is.Empty);
        }
    }
}
