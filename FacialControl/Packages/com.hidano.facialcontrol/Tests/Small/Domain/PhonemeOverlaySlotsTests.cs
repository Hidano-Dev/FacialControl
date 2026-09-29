using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    [SmallTest]
    public class PhonemeOverlaySlotsTests : SizedTestFixture
    {
        [TestCase(PhonemeOverlaySlots.A)]
        [TestCase(PhonemeOverlaySlots.I)]
        [TestCase(PhonemeOverlaySlots.U)]
        [TestCase(PhonemeOverlaySlots.E)]
        [TestCase(PhonemeOverlaySlots.O)]
        public void IsReserved_LowercaseFiveSlots_ReturnsTrue(string slot)
        {
            Assert.IsTrue(PhonemeOverlaySlots.IsReserved(slot));
        }

        [Test]
        public void IsReserved_UppercaseA_ReturnsFalse()
        {
            Assert.IsFalse(PhonemeOverlaySlots.IsReserved("A"));
        }

        [Test]
        public void MapReservedToPhonemeId_LowercaseA_ReturnsUppercaseA()
        {
            Assert.AreEqual("A", PhonemeOverlaySlots.MapReservedToPhonemeId(PhonemeOverlaySlots.A));
        }

        [Test]
        public void ReservedNames_Length_IsFive()
        {
            Assert.AreEqual(5, PhonemeOverlaySlots.ReservedNames.Length);
        }
    }
}
