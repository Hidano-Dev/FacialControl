using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerOverrideMaskTests
    {
        [Test]
        public void Combine_TwoFlags_HasBoth()
        {
            var mask = LayerOverrideMask.Bit0 | LayerOverrideMask.Bit2;

            Assert.IsTrue(mask.HasFlag(LayerOverrideMask.Bit0));
            Assert.IsTrue(mask.HasFlag(LayerOverrideMask.Bit2));
        }

        [Test]
        public void HasFlag_AbsentBit_ReturnsFalse()
        {
            var mask = LayerOverrideMask.Bit0 | LayerOverrideMask.Bit2;

            Assert.IsFalse(mask.HasFlag(LayerOverrideMask.Bit1));
            Assert.IsFalse(mask.HasFlag(LayerOverrideMask.Bit31));
        }

        [Test]
        public void None_HasNoBits_ReturnsTrue()
        {
            var mask = LayerOverrideMask.None;

            Assert.AreEqual(0, (int)mask);
            Assert.IsTrue(mask == LayerOverrideMask.None);
            Assert.IsFalse(mask.HasFlag(LayerOverrideMask.Bit0));
        }

        [Test]
        public void BitPositionCount_Equals32()
        {
            var values = Enum.GetValues(typeof(LayerOverrideMask));
            int bitCount = 0;
            foreach (LayerOverrideMask v in values)
            {
                if (v == LayerOverrideMask.None)
                {
                    continue;
                }
                bitCount++;
            }

            Assert.AreEqual(32, bitCount);
        }
    }
}
