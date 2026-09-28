using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// BlendShapeSnapshot のテスト。
    /// (RendererPath, Name, Value) を保持する readonly struct の振る舞いを検証する。
    /// </summary>
    [TestFixture]
    public class BlendShapeSnapshotTests
    {
        [Test]
        public void Ctor_Stores_AllFields()
        {
            var snapshot = new BlendShapeSnapshot("Body/Face", "Smile", 0.75f);

            Assert.AreEqual("Body/Face", snapshot.RendererPath);
            Assert.AreEqual("Smile", snapshot.Name);
            Assert.AreEqual(0.75f, snapshot.Value);
        }

        [Test]
        public void Equality_SameValues_AreEqual()
        {
            var a = new BlendShapeSnapshot("Body/Face", "Smile", 0.75f);
            var b = new BlendShapeSnapshot("Body/Face", "Smile", 0.75f);

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equality_DifferentRendererPath_AreNotEqual()
        {
            var a = new BlendShapeSnapshot("Body/Face", "Smile", 0.5f);
            var b = new BlendShapeSnapshot("Body/Head", "Smile", 0.5f);

            Assert.IsFalse(a.Equals(b));
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equality_DifferentName_AreNotEqual()
        {
            var a = new BlendShapeSnapshot("Body/Face", "Smile", 0.5f);
            var b = new BlendShapeSnapshot("Body/Face", "Anger", 0.5f);

            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Equality_DifferentValue_AreNotEqual()
        {
            var a = new BlendShapeSnapshot("Body/Face", "Smile", 0.5f);
            var b = new BlendShapeSnapshot("Body/Face", "Smile", 0.6f);

            Assert.IsFalse(a.Equals(b));
        }

        [Test]
        public void Ctor_NullStrings_NormalizedToEmpty()
        {
            var snapshot = new BlendShapeSnapshot(null, null, 0f);

            Assert.AreEqual(string.Empty, snapshot.RendererPath);
            Assert.AreEqual(string.Empty, snapshot.Name);
        }

        [Test]
        public void Ctor_JapaneseAndSpecialChars_StoresVerbatim()
        {
            var snapshot = new BlendShapeSnapshot("Body/顔", "笑顔_01", 1f);

            Assert.AreEqual("Body/顔", snapshot.RendererPath);
            Assert.AreEqual("笑顔_01", snapshot.Name);
        }
    }
}
