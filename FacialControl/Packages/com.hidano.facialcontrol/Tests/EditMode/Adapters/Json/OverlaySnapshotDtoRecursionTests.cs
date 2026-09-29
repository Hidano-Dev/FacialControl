using System.Collections.Generic;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    /// <summary>
    /// overlay snapshot 型の自己再帰断ち切りに関する型レベル契約テスト。
    /// <para>
    /// JsonUtility / Unity シリアライザは
    /// <c>ExpressionSnapshotDto.overlays → OverlaySlotBindingDto.snapshot → ExpressionSnapshotDto.overlays …</c>
    /// の自己再帰を depth 10 で打ち切り「Serialization depth limit 10 exceeded」警告を出していた。
    /// overlay binding が保持する snapshot 型を再帰終端型 <see cref="OverlaySnapshotDto"/>（overlays を持たない）に
    /// 変えることで、参照グラフを 1 段で止める。本テストはその型不変条件を検証する。
    /// </para>
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OverlaySnapshotDtoRecursionTests : SizedTestFixture
    {
        [Test]
        public void OverlaySnapshotDto_DoesNotExposeOverlaysField_TerminatesRecursion()
        {
            var field = typeof(OverlaySnapshotDto).GetField("overlays");

            Assert.That(field, Is.Null,
                "OverlaySnapshotDto must not declare an 'overlays' field; it is the recursion terminal type.");
        }

        [Test]
        public void OverlaySlotBindingDto_SnapshotField_IsOverlaySnapshotDto()
        {
            var field = typeof(OverlaySlotBindingDto).GetField("snapshot");

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(OverlaySnapshotDto)),
                "OverlaySlotBindingDto.snapshot must use the recursion-terminal type OverlaySnapshotDto.");
        }

        [Test]
        public void OverlaySlotBindingSerializable_CachedSnapshotField_IsOverlaySnapshotDto()
        {
            var field = typeof(OverlaySlotBindingSerializable).GetField("cachedSnapshot");

            Assert.That(field, Is.Not.Null);
            Assert.That(field.FieldType, Is.EqualTo(typeof(OverlaySnapshotDto)),
                "OverlaySlotBindingSerializable.cachedSnapshot must use the recursion-terminal type OverlaySnapshotDto.");
        }

        [Test]
        public void ExpressionSnapshotDto_InheritsOverlaySnapshotDto_AndStillDeclaresOverlays()
        {
            Assert.That(typeof(OverlaySnapshotDto).IsAssignableFrom(typeof(ExpressionSnapshotDto)), Is.True,
                "ExpressionSnapshotDto must inherit the common fields from OverlaySnapshotDto.");

            var overlaysField = typeof(ExpressionSnapshotDto).GetField("overlays");
            Assert.That(overlaysField, Is.Not.Null,
                "ExpressionSnapshotDto (expression/base level) must keep its 'overlays' field.");
            Assert.That(overlaysField.FieldType, Is.EqualTo(typeof(List<OverlaySlotBindingDto>)));
        }
    }
}
