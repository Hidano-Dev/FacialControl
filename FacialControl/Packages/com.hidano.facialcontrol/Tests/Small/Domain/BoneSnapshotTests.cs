using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// BoneSnapshot のテスト。
    /// (BonePath, PositionXYZ, EulerXYZ, ScaleXYZ) の 9 float + 1 string を保持する readonly struct の振る舞いを検証する。
    /// </summary>
    [TestFixture]
    public class BoneSnapshotTests
    {
        [Test]
        public void Ctor_Stores_AllNineFloats()
        {
            var snapshot = new BoneSnapshot(
                "Armature/Hips/Spine/Head",
                positionX: 0.1f, positionY: 0.2f, positionZ: 0.3f,
                eulerX: 10f, eulerY: 20f, eulerZ: 30f,
                scaleX: 1f, scaleY: 1.5f, scaleZ: 2f);

            Assert.AreEqual("Armature/Hips/Spine/Head", snapshot.BonePath);
            Assert.AreEqual(0.1f, snapshot.PositionX);
            Assert.AreEqual(0.2f, snapshot.PositionY);
            Assert.AreEqual(0.3f, snapshot.PositionZ);
            Assert.AreEqual(10f, snapshot.EulerX);
            Assert.AreEqual(20f, snapshot.EulerY);
            Assert.AreEqual(30f, snapshot.EulerZ);
            Assert.AreEqual(1f, snapshot.ScaleX);
            Assert.AreEqual(1.5f, snapshot.ScaleY);
            Assert.AreEqual(2f, snapshot.ScaleZ);
        }

        [Test]
        public void Equality_SameValues_AreEqual()
        {
            var a = new BoneSnapshot("Head", 1f, 2f, 3f, 10f, 20f, 30f, 1f, 1f, 1f);
            var b = new BoneSnapshot("Head", 1f, 2f, 3f, 10f, 20f, 30f, 1f, 1f, 1f);

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentBonePath_AreNotEqual()
        {
            var a = new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);
            var b = new BoneSnapshot("Neck", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);

            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Equality_DifferentPosition_AreNotEqual()
        {
            var a = new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);
            var b = new BoneSnapshot("Head", 0.1f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);

            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Equality_DifferentEuler_AreNotEqual()
        {
            var a = new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);
            var b = new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 5f, 0f, 1f, 1f, 1f);

            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Equality_DifferentScale_AreNotEqual()
        {
            var a = new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);
            var b = new BoneSnapshot("Head", 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 2f);

            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Ctor_NullBonePath_NormalizedToEmpty()
        {
            var snapshot = new BoneSnapshot(null, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f);

            Assert.AreEqual(string.Empty, snapshot.BonePath);
        }

        [Test]
        public void Ctor_NegativeAndLargeValues_RoundTrip()
        {
            var snapshot = new BoneSnapshot(
                "Bone",
                positionX: -1.5f, positionY: -2.5f, positionZ: -3.5f,
                eulerX: -180f, eulerY: 359.999f, eulerZ: 0f,
                scaleX: 0f, scaleY: -1f, scaleZ: 100f);

            Assert.AreEqual(-1.5f, snapshot.PositionX);
            Assert.AreEqual(-2.5f, snapshot.PositionY);
            Assert.AreEqual(-3.5f, snapshot.PositionZ);
            Assert.AreEqual(-180f, snapshot.EulerX);
            Assert.AreEqual(359.999f, snapshot.EulerY);
            Assert.AreEqual(0f, snapshot.EulerZ);
            Assert.AreEqual(0f, snapshot.ScaleX);
            Assert.AreEqual(-1f, snapshot.ScaleY);
            Assert.AreEqual(100f, snapshot.ScaleZ);
        }
    }
}
