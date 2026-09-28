using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class LayerDefinitionTests
    {
        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var layer = new LayerDefinition("emotion", 0, ExclusionMode.LastWins);

            Assert.AreEqual("emotion", layer.Name);
            Assert.AreEqual(0, layer.Priority);
            Assert.AreEqual(ExclusionMode.LastWins, layer.ExclusionMode);
        }

        [Test]
        public void Constructor_BlendMode_CreatesInstance()
        {
            var layer = new LayerDefinition("lipsync", 1, ExclusionMode.Blend);

            Assert.AreEqual("lipsync", layer.Name);
            Assert.AreEqual(1, layer.Priority);
            Assert.AreEqual(ExclusionMode.Blend, layer.ExclusionMode);
        }

        [Test]
        public void Constructor_HighPriority_CreatesInstance()
        {
            var layer = new LayerDefinition("eye", 100, ExclusionMode.LastWins);

            Assert.AreEqual(100, layer.Priority);
        }

        [Test]
        public void Constructor_NullName_ThrowsArgumentNullException()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                new LayerDefinition(null, 0, ExclusionMode.LastWins));
        }

        [Test]
        public void Constructor_EmptyName_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new LayerDefinition("", 0, ExclusionMode.LastWins));
        }

        [Test]
        public void Constructor_WhitespaceName_ThrowsArgumentException()
        {
            Assert.Throws<System.ArgumentException>(() =>
                new LayerDefinition("   ", 0, ExclusionMode.LastWins));
        }

        [Test]
        public void Constructor_NegativePriority_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new LayerDefinition("emotion", -1, ExclusionMode.LastWins));
        }

        [Test]
        public void Constructor_NegativePriorityLarge_ThrowsArgumentOutOfRangeException()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                new LayerDefinition("emotion", -100, ExclusionMode.Blend));
        }

        [Test]
        public void Constructor_PriorityZero_IsValid()
        {
            var layer = new LayerDefinition("emotion", 0, ExclusionMode.LastWins);

            Assert.AreEqual(0, layer.Priority);
        }

        [Test]
        public void Constructor_JapaneseName_CreatesInstance()
        {
            var layer = new LayerDefinition("感情", 0, ExclusionMode.LastWins);

            Assert.AreEqual("感情", layer.Name);
        }

        [Test]
        public void Constructor_SpecialCharacterName_CreatesInstance()
        {
            var layer = new LayerDefinition("layer-01_test", 2, ExclusionMode.Blend);

            Assert.AreEqual("layer-01_test", layer.Name);
        }
    }
}
