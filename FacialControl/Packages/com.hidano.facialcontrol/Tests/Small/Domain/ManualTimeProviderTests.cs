using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Tests.Shared;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    [SmallTest]
    public class ManualTimeProviderTests : SizedTestFixture
    {
        [Test]
        public void UnscaledTimeSeconds_AfterConstruction_IsZero()
        {
            var provider = new ManualTimeProvider();

            Assert.AreEqual(0.0, provider.UnscaledTimeSeconds);
        }

        [Test]
        public void UnscaledTimeSeconds_AfterAssignment_ReturnsSameValue()
        {
            var provider = new ManualTimeProvider();

            provider.UnscaledTimeSeconds = 1.5;

            Assert.AreEqual(1.5, provider.UnscaledTimeSeconds);
        }

        [Test]
        public void UnscaledTimeSeconds_SecondAssignment_OverridesPreviousValue()
        {
            var provider = new ManualTimeProvider();
            provider.UnscaledTimeSeconds = 1.0;

            provider.UnscaledTimeSeconds = 2.5;

            Assert.AreEqual(2.5, provider.UnscaledTimeSeconds);
        }

        [Test]
        public void ManualTimeProvider_IsAssignableToITimeProvider()
        {
            ITimeProvider provider = new ManualTimeProvider { UnscaledTimeSeconds = 3.25 };

            Assert.AreEqual(3.25, provider.UnscaledTimeSeconds);
        }
    }
}
