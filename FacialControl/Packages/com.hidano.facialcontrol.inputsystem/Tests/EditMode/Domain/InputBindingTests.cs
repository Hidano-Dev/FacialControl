using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    [SmallTest]
    public class InputBindingTests : SizedTestFixture
    {
        // --- 正常系 ---

        [Test]
        public void Constructor_ValidParameters_CreatesInstance()
        {
            var binding = new InputBinding("Trigger1", "expression-id-001");

            Assert.AreEqual("Trigger1", binding.ActionName);
            Assert.AreEqual("expression-id-001", binding.ExpressionId);
        }

        // --- 異常系: ActionName ---

        [Test]
        public void Constructor_NullActionName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new InputBinding(null, "expression-id"));
        }

        [Test]
        public void Constructor_EmptyActionName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new InputBinding(string.Empty, "expression-id"));
        }

        [Test]
        public void Constructor_WhitespaceActionName_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new InputBinding("   ", "expression-id"));
        }

        // --- 異常系: ExpressionId ---

        [Test]
        public void Constructor_NullExpressionId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new InputBinding("Trigger1", null));
        }

        [Test]
        public void Constructor_EmptyExpressionId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new InputBinding("Trigger1", string.Empty));
        }

        [Test]
        public void Constructor_WhitespaceExpressionId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new InputBinding("Trigger1", "   "));
        }

        // --- 等価性 ---

        [Test]
        public void Equals_SameValues_ReturnsTrue()
        {
            var a = new InputBinding("Trigger1", "expression-id");
            var b = new InputBinding("Trigger1", "expression-id");

            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void Equals_DifferentActionName_ReturnsFalse()
        {
            var a = new InputBinding("Trigger1", "expression-id");
            var b = new InputBinding("Trigger2", "expression-id");

            Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equals_DifferentExpressionId_ReturnsFalse()
        {
            var a = new InputBinding("Trigger1", "expression-id-001");
            var b = new InputBinding("Trigger1", "expression-id-002");

            Assert.IsFalse(a.Equals(b));
            Assert.IsFalse(a == b);
            Assert.IsTrue(a != b);
        }

        [Test]
        public void Equals_ObjectOverride_WorksCorrectly()
        {
            var a = new InputBinding("Trigger1", "expression-id");
            object b = new InputBinding("Trigger1", "expression-id");
            object other = "not-a-binding";

            Assert.IsTrue(a.Equals(b));
            Assert.IsFalse(a.Equals(other));
        }

        // --- アセンブリ境界 ---

        [Test]
        public void Type_IsDefinedInDomainAssembly_WithoutUnityEngineDependency()
        {
            var type = typeof(InputBinding);

            Assert.AreEqual("Hidano.FacialControl.Domain.Models", type.Namespace);

            var assemblyName = type.Assembly.GetName().Name;
            Assert.AreEqual("Hidano.FacialControl.Domain", assemblyName);

            foreach (var referenced in type.Assembly.GetReferencedAssemblies())
            {
                Assert.AreNotEqual(
                    "UnityEngine",
                    referenced.Name,
                    "Domain アセンブリは UnityEngine を直接参照してはならない。");
            }
        }
    }
}
