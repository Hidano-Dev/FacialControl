using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class AddressPresetKindTests : SizedTestFixture
    {
        [Test]
        public void EnumValues_SerializedValues_AreStable()
        {
            Assert.AreEqual(0, (int)AddressPresetKind.VRChat);
            Assert.AreEqual(1, (int)AddressPresetKind.ARKit);
            Assert.AreEqual(2, (int)AddressPresetKind.Custom);
        }
    }
}
