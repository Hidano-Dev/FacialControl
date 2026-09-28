using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class OverlaySlotBindingTests
    {
        [Test]
        public void Constructor_DefaultFallback_KeepsSlotAndState()
        {
            var binding = new OverlaySlotBinding("blink", suppress: false, snapshot: null);

            Assert.AreEqual("blink", binding.Slot);
            Assert.IsFalse(binding.Suppress);
            Assert.IsFalse(binding.Snapshot.HasValue);
            Assert.IsTrue(binding.IsDefaultFallback);
        }

        [Test]
        public void Constructor_Suppress_KeepsSlotAndState()
        {
            var binding = new OverlaySlotBinding("blink", suppress: true, snapshot: null);

            Assert.AreEqual("blink", binding.Slot);
            Assert.IsTrue(binding.Suppress);
            Assert.IsFalse(binding.Snapshot.HasValue);
            Assert.IsFalse(binding.IsDefaultFallback);
        }

        [Test]
        public void Constructor_SnapshotOverride_KeepsSlotAndState()
        {
            var snapshot = CreateSnapshot("blink-override");

            var binding = new OverlaySlotBinding("blink", suppress: false, snapshot: snapshot);

            Assert.AreEqual("blink", binding.Slot);
            Assert.IsFalse(binding.Suppress);
            Assert.IsTrue(binding.Snapshot.HasValue);
            Assert.AreEqual(snapshot, binding.Snapshot.Value);
            Assert.IsFalse(binding.IsDefaultFallback);
        }

        [Test]
        public void Constructor_SuppressWithSnapshot_ThrowsArgumentException()
        {
            var snapshot = CreateSnapshot("blink-override");

            Assert.Throws<ArgumentException>(() =>
                new OverlaySlotBinding("blink", suppress: true, snapshot: snapshot));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_EmptySlot_ThrowsArgumentException(string slot)
        {
            Assert.Throws<ArgumentException>(() =>
                new OverlaySlotBinding(slot, suppress: false, snapshot: null));
        }

        [Test]
        public void Equals_SameSlotSuppressAndSnapshot_AreEqual()
        {
            var snapshot = CreateSnapshot("blink-override");
            var a = new OverlaySlotBinding("blink", suppress: false, snapshot: snapshot);
            var b = new OverlaySlotBinding("blink", suppress: false, snapshot: snapshot);

            Assert.IsTrue(a.Equals(b));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equals_DifferentState_AreNotEqual()
        {
            var fallback = new OverlaySlotBinding("blink", suppress: false, snapshot: null);
            var suppress = new OverlaySlotBinding("blink", suppress: true, snapshot: null);
            var overrideSnapshot = new OverlaySlotBinding(
                "blink",
                suppress: false,
                snapshot: CreateSnapshot("blink-override"));

            Assert.IsFalse(fallback.Equals(suppress));
            Assert.IsFalse(fallback.Equals(overrideSnapshot));
            Assert.IsFalse(suppress.Equals(overrideSnapshot));
        }

        private static ExpressionSnapshot CreateSnapshot(string id)
        {
            return new ExpressionSnapshot(
                id,
                transitionDuration: Expression.DefaultTransitionDuration,
                transitionCurvePreset: TransitionCurvePreset.Linear,
                blendShapes: new[]
                {
                    new BlendShapeSnapshot("Body/Face", "Blink", 100f),
                },
                bones: null,
                rendererPaths: new[] { "Body/Face" });
        }
    }
}
