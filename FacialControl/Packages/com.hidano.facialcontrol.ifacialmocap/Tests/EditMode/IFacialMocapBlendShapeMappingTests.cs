using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.IFacialMocap;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class IFacialMocapBlendShapeMappingTests : SizedTestFixture
    {
        [Test]
        public void Default_LegacyZeroInitialized_IsEnabledWithIdentityTuning()
        {
            // 調整項目の追加前に保存されたアセット / Inspector の「+」で 0 初期化された要素。
            var mapping = new IFacialMocapBlendShapeMapping { ifacialMocapName = "jawOpen", blendShapeName = "jawOpen" };

            Assert.That(mapping.HasTuning, Is.False);
            Assert.That(mapping.EffectiveEnabled, Is.True);
            AssertIdentity(mapping.EffectiveTuning);
        }

        [Test]
        public void Ctor_NamesOnly_IsEnabledWithIdentityTuning()
        {
            var mapping = new IFacialMocapBlendShapeMapping("jawOpen", "jawOpen");

            Assert.That(mapping.HasTuning, Is.True);
            Assert.That(mapping.EffectiveEnabled, Is.True);
            AssertIdentity(mapping.EffectiveTuning);
        }

        [Test]
        public void Ctor_WithTuning_ExposesStoredValues()
        {
            var mapping = new IFacialMocapBlendShapeMapping("jawOpen", "jawOpen", false, 0.1f, 0.9f, 0.5f);

            Assert.That(mapping.EffectiveEnabled, Is.False);
            Assert.That(mapping.EffectiveTuning.Min, Is.EqualTo(0.1f));
            Assert.That(mapping.EffectiveTuning.Max, Is.EqualTo(0.9f));
            Assert.That(mapping.EffectiveTuning.Weight, Is.EqualTo(0.5f));
        }

        private static void AssertIdentity(IFacialMocapValueTuning tuning)
        {
            Assert.That(tuning.Min, Is.EqualTo(0f));
            Assert.That(tuning.Max, Is.EqualTo(1f));
            Assert.That(tuning.Weight, Is.EqualTo(1f));
        }
    }
}
