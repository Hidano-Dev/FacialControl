using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class InvalidSlotReferenceTests
    {
        [Test]
        public void Constructor_DuplicateReason_StoresFields()
        {
            var reference = new InvalidSlotReference("blink", InvalidSlotReference.DuplicateReason);

            Assert.AreEqual("blink", reference.Slot);
            Assert.AreEqual(InvalidSlotReference.DuplicateReason, reference.Reason);
        }

        [Test]
        public void Constructor_UndeclaredReason_StoresFields()
        {
            var reference = new InvalidSlotReference("blink", InvalidSlotReference.UndeclaredReason);

            Assert.AreEqual("blink", reference.Slot);
            Assert.AreEqual(InvalidSlotReference.UndeclaredReason, reference.Reason);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("Other")]
        [TestCase("duplicate")]
        public void Constructor_UnknownReason_ThrowsArgumentException(string reason)
        {
            Assert.Throws<ArgumentException>(() => new InvalidSlotReference("blink", reason));
        }

        [Test]
        public void Equality_SameSlotAndReason_AreEqual()
        {
            var a = new InvalidSlotReference("blink", InvalidSlotReference.UndeclaredReason);
            var b = new InvalidSlotReference("blink", InvalidSlotReference.UndeclaredReason);

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentReason_AreNotEqual()
        {
            var a = new InvalidSlotReference("blink", InvalidSlotReference.DuplicateReason);
            var b = new InvalidSlotReference("blink", InvalidSlotReference.UndeclaredReason);

            Assert.IsFalse(a.Equals(b));
        }
    }
}
