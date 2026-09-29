using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [TestFixture]
    [SmallTest]
    public class LayerPriorityNormalizerTests : SizedTestFixture
    {
        [Test]
        public void Normalize_DistinctAscending_ReturnsSameValues()
        {
            int[] result = LayerPriorityNormalizer.Normalize(new[] { 0, 5, 10 });

            CollectionAssert.AreEqual(new[] { 0, 5, 10 }, result);
        }

        [Test]
        public void Normalize_Duplicate_ShiftsLaterValueUpwardMinimally()
        {
            int[] result = LayerPriorityNormalizer.Normalize(new[] { 5, 5 });

            CollectionAssert.AreEqual(new[] { 5, 6 }, result);
        }

        [Test]
        public void Normalize_DuplicatePreservingGaps_OnlyNudgesCollisions()
        {
            int[] result = LayerPriorityNormalizer.Normalize(new[] { 0, 5, 5, 10 });

            CollectionAssert.AreEqual(new[] { 0, 5, 6, 10 }, result);
        }

        [Test]
        public void Normalize_AllSame_ProducesSequential()
        {
            int[] result = LayerPriorityNormalizer.Normalize(new[] { 3, 3, 3 });

            CollectionAssert.AreEqual(new[] { 3, 4, 5 }, result);
        }

        [Test]
        public void Normalize_NegativeValues_ClampedToZeroThenDistinct()
        {
            int[] result = LayerPriorityNormalizer.Normalize(new[] { -1, -1 });

            CollectionAssert.AreEqual(new[] { 0, 1 }, result);
        }

        [Test]
        public void Normalize_UnsortedInput_RespectsPriorityOrderForCorrection()
        {
            // 整列順: idx1(2) -> idx0(5) -> idx2(5)。idx2 のみ 6 へ押し上げる。
            int[] result = LayerPriorityNormalizer.Normalize(new[] { 5, 2, 5 });

            CollectionAssert.AreEqual(new[] { 5, 2, 6 }, result);
        }

        [Test]
        public void Normalize_Empty_ReturnsEmpty()
        {
            int[] result = LayerPriorityNormalizer.Normalize(new int[0]);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void RequiresCorrection_DistinctValues_ReturnsFalse()
        {
            Assert.That(LayerPriorityNormalizer.RequiresCorrection(new[] { 0, 1, 2 }), Is.False);
        }

        [Test]
        public void RequiresCorrection_DuplicateValues_ReturnsTrue()
        {
            Assert.That(LayerPriorityNormalizer.RequiresCorrection(new[] { 0, 0, 2 }), Is.True);
        }
    }
}
