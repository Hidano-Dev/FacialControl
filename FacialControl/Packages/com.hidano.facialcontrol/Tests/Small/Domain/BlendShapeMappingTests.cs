using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class BlendShapeMappingTests
    {
        // --- 正常系 ---

        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var mapping = new BlendShapeMapping("Blink_L", 0.5f, "Face");

            Assert.AreEqual("Blink_L", mapping.Name);
            Assert.AreEqual(0.5f, mapping.Value);
            Assert.AreEqual("Face", mapping.Renderer);
        }

        [Test]
        public void Constructor_RendererNull_CreatesInstance()
        {
            var mapping = new BlendShapeMapping("Blink_L", 0.5f, null);

            Assert.AreEqual("Blink_L", mapping.Name);
            Assert.AreEqual(0.5f, mapping.Value);
            Assert.IsNull(mapping.Renderer);
        }

        [Test]
        public void Constructor_RendererOmitted_DefaultsToNull()
        {
            var mapping = new BlendShapeMapping("Blink_L", 0.5f);

            Assert.IsNull(mapping.Renderer);
        }

        // --- 値クランプ ---

        [Test]
        public void Constructor_ValueZero_ReturnsZero()
        {
            var mapping = new BlendShapeMapping("Blink_L", 0f);

            Assert.AreEqual(0f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueOne_ReturnsOne()
        {
            var mapping = new BlendShapeMapping("Blink_L", 1f);

            Assert.AreEqual(1f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueAboveOne_ClampedToOne()
        {
            var mapping = new BlendShapeMapping("Blink_L", 1.5f);

            Assert.AreEqual(1f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueBelowZero_ClampedToZero()
        {
            var mapping = new BlendShapeMapping("Blink_L", -0.5f);

            Assert.AreEqual(0f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueLargePositive_ClampedToOne()
        {
            var mapping = new BlendShapeMapping("Blink_L", 100f);

            Assert.AreEqual(1f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueLargeNegative_ClampedToZero()
        {
            var mapping = new BlendShapeMapping("Blink_L", -100f);

            Assert.AreEqual(0f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueSlightlyAboveOne_ClampedToOne()
        {
            var mapping = new BlendShapeMapping("Blink_L", 1.0001f);

            Assert.AreEqual(1f, mapping.Value);
        }

        [Test]
        public void Constructor_ValueSlightlyBelowZero_ClampedToZero()
        {
            var mapping = new BlendShapeMapping("Blink_L", -0.0001f);

            Assert.AreEqual(0f, mapping.Value);
        }

        // --- Name バリデーション ---

        [Test]
        public void Constructor_NullName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new BlendShapeMapping(null, 0.5f));
        }

        // --- 2バイト文字・特殊記号対応 ---

        [Test]
        public void Constructor_JapaneseName_CreatesInstance()
        {
            var mapping = new BlendShapeMapping("まばたき左", 0.8f);

            Assert.AreEqual("まばたき左", mapping.Name);
        }

        [Test]
        public void Constructor_SpecialCharacterName_CreatesInstance()
        {
            var mapping = new BlendShapeMapping("Blink_L-01.test", 0.3f);

            Assert.AreEqual("Blink_L-01.test", mapping.Name);
        }

        // --- Renderer null 許容の追加テスト ---

        [Test]
        public void Constructor_WithRenderer_StoresRendererName()
        {
            var mapping = new BlendShapeMapping("Blink_L", 0.5f, "Body");

            Assert.AreEqual("Body", mapping.Renderer);
        }

        [Test]
        public void Constructor_NullRendererExplicit_IsNull()
        {
            var mapping = new BlendShapeMapping("Blink_L", 0.5f, null);

            Assert.IsNull(mapping.Renderer);
        }
    }
}
