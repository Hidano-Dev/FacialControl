using Hidano.FacialControl.Adapters.IFacialMocap;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public class IFacialMocapValueTuningTests : SizedTestFixture
    {
        [TestCase(0f)]
        [TestCase(0.25f)]
        [TestCase(0.5f)]
        [TestCase(1f)]
        public void Apply_Identity_ReturnsInputUnchanged(float value)
        {
            Assert.That(IFacialMocapValueTuning.Identity.Apply(value), Is.EqualTo(value).Within(1e-6f));
        }

        [TestCase(0.1f, 0f)]
        [TestCase(0.2f, 0f)]
        [TestCase(0.5f, 0.5f)]
        [TestCase(0.8f, 1f)]
        [TestCase(0.9f, 1f)]
        public void Apply_Range_RemapsInverseLerpAndClamps(float value, float expected)
        {
            var tuning = new IFacialMocapValueTuning(0.2f, 0.8f, 1f);

            Assert.That(tuning.Apply(value), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void Apply_Weight_MultipliesAfterRemap()
        {
            var tuning = new IFacialMocapValueTuning(0.2f, 0.8f, 0.5f);

            Assert.That(tuning.Apply(0.5f), Is.EqualTo(0.25f).Within(1e-5f));
        }

        [Test]
        public void Apply_WeightAboveOne_ClampsResultToOne()
        {
            var tuning = new IFacialMocapValueTuning(0f, 1f, 3f);

            Assert.That(tuning.Apply(0.5f), Is.EqualTo(1f));
        }

        [Test]
        public void Apply_NegativeWeight_ClampsResultToZero()
        {
            var tuning = new IFacialMocapValueTuning(0f, 1f, -1f);

            Assert.That(tuning.Apply(0.5f), Is.EqualTo(0f));
        }

        [TestCase(0.29f, 0f)]
        [TestCase(0.3f, 1f)]
        [TestCase(0.9f, 1f)]
        public void Apply_MinEqualsMax_ActsAsStep(float value, float expected)
        {
            var tuning = new IFacialMocapValueTuning(0.3f, 0.3f, 1f);

            Assert.That(tuning.Apply(value), Is.EqualTo(expected));
        }

        [Test]
        public void Ctor_MinGreaterThanMax_SwapsBounds()
        {
            var tuning = new IFacialMocapValueTuning(0.8f, 0.2f, 1f);

            Assert.That(tuning.Min, Is.EqualTo(0.2f));
            Assert.That(tuning.Max, Is.EqualTo(0.8f));
        }

        [Test]
        public void Ctor_OutOfUnitRange_ClampsBounds()
        {
            var tuning = new IFacialMocapValueTuning(-0.5f, 2f, 1f);

            Assert.That(tuning.Min, Is.EqualTo(0f));
            Assert.That(tuning.Max, Is.EqualTo(1f));
        }
    }
}
