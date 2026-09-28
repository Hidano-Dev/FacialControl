using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// ExpressionTests。
    /// スキーマ (Id / Name / Layer / OverrideMask: LayerOverrideMask / SnapshotId: string) を中心に検証する。
    /// </summary>
    [TestFixture]
    public class ExpressionTests
    {
        // --- 新スキーマ: Ctor_StoresAllFields ---

        [Test]
        public void Ctor_StoresAllFields()
        {
            var expression = new Expression(
                id: "expr-id-001",
                name: "smile",
                layer: "emotion",
                overrideMask: LayerOverrideMask.Bit0 | LayerOverrideMask.Bit2,
                snapshotId: "snap-id-001");

            Assert.AreEqual("expr-id-001", expression.Id);
            Assert.AreEqual("smile", expression.Name);
            Assert.AreEqual("emotion", expression.Layer);
            Assert.AreEqual(LayerOverrideMask.Bit0 | LayerOverrideMask.Bit2, expression.OverrideMask);
            Assert.AreEqual("snap-id-001", expression.SnapshotId);
        }

        // --- 新スキーマ: OverrideMask は None でも Domain 上は許容（zero-mask validation は Adapters/Editor 層で実施） ---

        [Test]
        public void OverrideMask_DefaultsToNone_AllowedByDomain()
        {
            var expression = new Expression(
                id: "expr-id-002",
                name: "neutral",
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "snap-id-002");

            Assert.AreEqual(LayerOverrideMask.None, expression.OverrideMask);
        }

        // --- 新スキーマ: SnapshotId は非空必須 ---

        [Test]
        public void SnapshotId_NonEmpty()
        {
            Assert.Throws<ArgumentException>(() => new Expression(
                id: "expr-id-003",
                name: "broken",
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: ""));
        }

        [Test]
        public void SnapshotId_Whitespace_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Expression(
                id: "expr-id-004",
                name: "broken-ws",
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "   "));
        }

        [Test]
        public void SnapshotId_Null_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Expression(
                id: "expr-id-005",
                name: "broken-null",
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: null));
        }

        // --- 新スキーマ: 既存 Id/Name/Layer バリデーションも維持 ---

        [Test]
        public void Ctor_NullId_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Expression(
                id: null,
                name: "smile",
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "snap"));
        }

        [Test]
        public void Ctor_EmptyId_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Expression(
                id: "",
                name: "smile",
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "snap"));
        }

        [Test]
        public void Ctor_NullName_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new Expression(
                id: "expr-id-x",
                name: null,
                layer: "emotion",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "snap"));
        }

        [Test]
        public void Ctor_EmptyLayer_ThrowsArgumentException()
        {
            Assert.Throws<ArgumentException>(() => new Expression(
                id: "expr-id-x",
                name: "smile",
                layer: "",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "snap"));
        }

        // --- Refactor: ToString() フォーマット ---

        [Test]
        public void ToString_ReturnsIdNameLayerFormat()
        {
            var expression = new Expression(
                id: "expr-007",
                name: "wink",
                layer: "eye",
                overrideMask: LayerOverrideMask.None,
                snapshotId: "snap-007");

            Assert.AreEqual("expr-007:wink@eye", expression.ToString());
        }

        // --- 新スキーマ: 多 bit OverrideMask の保持 ---

        [Test]
        public void OverrideMask_MultipleBits_Preserved()
        {
            var mask = LayerOverrideMask.Bit0 | LayerOverrideMask.Bit3 | LayerOverrideMask.Bit7;
            var expression = new Expression(
                id: "expr-008",
                name: "complex",
                layer: "emotion",
                overrideMask: mask,
                snapshotId: "snap-008");

            Assert.AreEqual(mask, expression.OverrideMask);
            Assert.IsTrue((expression.OverrideMask & LayerOverrideMask.Bit0) != 0);
            Assert.IsTrue((expression.OverrideMask & LayerOverrideMask.Bit3) != 0);
            Assert.IsTrue((expression.OverrideMask & LayerOverrideMask.Bit7) != 0);
        }
    }
}
