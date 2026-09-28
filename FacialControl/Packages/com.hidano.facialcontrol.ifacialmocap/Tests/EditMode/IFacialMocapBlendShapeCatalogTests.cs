using Hidano.FacialControl.Adapters.IFacialMocap;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class IFacialMocapBlendShapeCatalogTests : SizedTestFixture
    {
        [Test]
        public void Names_Contains52ArKitBlendShapes()
        {
            Assert.That(IFacialMocapBlendShapeCatalog.Count, Is.EqualTo(52));
            Assert.That(IFacialMocapBlendShapeCatalog.Names.Length, Is.EqualTo(52));
        }

        [Test]
        public void ToArKitName_LeftSuffix_BecomesLeft()
        {
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("eyeBlink_L"), Is.EqualTo("eyeBlinkLeft"));
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("mouthSmile_L"), Is.EqualTo("mouthSmileLeft"));
        }

        [Test]
        public void ToArKitName_RightSuffix_BecomesRight()
        {
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("eyeBlink_R"), Is.EqualTo("eyeBlinkRight"));
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("browOuterUp_R"), Is.EqualTo("browOuterUpRight"));
        }

        [Test]
        public void ToArKitName_NoLateralitySuffix_IsUnchanged()
        {
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("jawOpen"), Is.EqualTo("jawOpen"));
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("mouthLeft"), Is.EqualTo("mouthLeft"));
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("jawRight"), Is.EqualTo("jawRight"));
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("browInnerUp"), Is.EqualTo("browInnerUp"));
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName("tongueOut"), Is.EqualTo("tongueOut"));
        }

        [Test]
        public void ToArKitName_NullOrEmpty_ReturnsInput()
        {
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName(null), Is.Null);
            Assert.That(IFacialMocapBlendShapeCatalog.ToArKitName(string.Empty), Is.EqualTo(string.Empty));
        }
    }
}
