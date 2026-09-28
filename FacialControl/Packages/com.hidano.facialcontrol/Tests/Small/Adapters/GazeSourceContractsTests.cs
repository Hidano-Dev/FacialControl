using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Domain.Adapters;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    public sealed class GazeSourceContractsTests
    {
        [Test]
        public void GazeSourceDeclaration_PreservesChannelAndPairValues()
        {
            var declaration = new GazeSourceDeclaration("custom", true);

            Assert.That(declaration.ChannelId, Is.EqualTo("custom"));
            Assert.That(declaration.ProvidesLeftRightPair, Is.True);
        }

        [Test]
        public void GazeSourceDeclaration_AllowsNullAndEmptyChannelAsWildcard()
        {
            var nullDeclaration = new GazeSourceDeclaration(null, false);
            var emptyDeclaration = new GazeSourceDeclaration(string.Empty, true);

            Assert.That(nullDeclaration.ChannelId, Is.Null);
            Assert.That(emptyDeclaration.ChannelId, Is.Empty);
        }

        [Test]
        public void GazeContracts_ExposeTypedDeclarationAndInjectionMembers()
        {
            IGazeSourceProvider provider = new FakeGazeSourceProvider();
            IGazeChannelConsumer consumer = new FakeGazeChannelConsumer();

            Assert.That(provider.GetGazeSourceDeclarations().Count(), Is.EqualTo(1));
            consumer.ConfigureGazeChannels(new[] { "gaze", "camera" });
            Assert.That(((FakeGazeChannelConsumer)consumer).ChannelIds,
                Is.EqualTo(new[] { "gaze", "camera" }));
        }

        private sealed class FakeGazeSourceProvider : IGazeSourceProvider
        {
            public IEnumerable<GazeSourceDeclaration> GetGazeSourceDeclarations()
            {
                yield return new GazeSourceDeclaration("gaze", true);
            }
        }

        private sealed class FakeGazeChannelConsumer : IGazeChannelConsumer
        {
            public IReadOnlyList<string> ChannelIds { get; private set; }

            public void ConfigureGazeChannels(IReadOnlyList<string> channelIds)
            {
                ChannelIds = channelIds;
            }
        }
    }
}
