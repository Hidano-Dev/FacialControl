using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Tests.Shared;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class ITimeProviderContractTests
    {
        [Test]
        public void UnscaledTimeSeconds_IsReadableFromContract()
        {
            ITimeProvider provider = new ManualTimeProvider { UnscaledTimeSeconds = 0.75 };

            Assert.AreEqual(0.75, provider.UnscaledTimeSeconds);
        }

        [Test]
        public void UnscaledTimeSeconds_WhenAdvancedFrom1To2_IsMonotonicallyIncreasing()
        {
            ITimeProvider provider = new ManualTimeProvider();

            ((ManualTimeProvider)provider).UnscaledTimeSeconds = 1.0;
            double first = provider.UnscaledTimeSeconds;

            ((ManualTimeProvider)provider).UnscaledTimeSeconds = 2.0;
            double second = provider.UnscaledTimeSeconds;

            Assert.Greater(second, first);
        }

        [Test]
        public void UnscaledTimeSeconds_AcrossMultipleAdvances_NeverDecreases()
        {
            var fake = new ManualTimeProvider();
            ITimeProvider provider = fake;

            double previous = provider.UnscaledTimeSeconds;

            foreach (double t in new[] { 0.5, 1.0, 1.25, 3.5, 10.0 })
            {
                fake.UnscaledTimeSeconds = t;
                double current = provider.UnscaledTimeSeconds;

                Assert.GreaterOrEqual(current, previous);
                previous = current;
            }
        }
    }
}
