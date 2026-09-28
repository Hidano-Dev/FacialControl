using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public sealed class GazeSourceIdConventionTests
    {
        [TestCase("receiver", "gaze", GazeSide.Shared)]
        [TestCase("receiver", "gaze", GazeSide.Left)]
        [TestCase("receiver", "secondary", GazeSide.Right)]
        public void ComposeThenTryParse_RoundTrips(string slug, string channelId, GazeSide side)
        {
            string sourceId = GazeSourceIdConvention.Compose(slug, channelId, side);

            Assert.That(
                GazeSourceIdConvention.TryParse(
                    sourceId,
                    out string parsedSlug,
                    out string parsedChannelId,
                    out GazeSide parsedSide),
                Is.True);
            Assert.That(parsedSlug, Is.EqualTo(slug));
            Assert.That(parsedChannelId, Is.EqualTo(channelId));
            Assert.That(parsedSide, Is.EqualTo(side));
        }

        [Test]
        public void DefaultChannelId_AndLeftComposition_AreStable()
        {
            Assert.That(GazeSourceIdConvention.DefaultChannelId, Is.EqualTo("gaze"));
            Assert.That(
                GazeSourceIdConvention.Compose("ifm", GazeSourceIdConvention.DefaultChannelId, GazeSide.Left),
                Is.EqualTo("ifm:gaze.left"));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase(":")]
        [TestCase("receiver:gaze:left")]
        [TestCase("receiver:.left")]
        public void TryParse_NonConformingInput_ReturnsFalse(string sourceId)
        {
            Assert.That(
                GazeSourceIdConvention.TryParse(sourceId, out _, out _, out _),
                Is.False);
        }

        [TestCase("")]
        [TestCase("gaze.left")]
        [TestCase("gaze:right")]
        [TestCase("gaze\u00a0")]
        public void IsValidChannelId_RejectsInvalidBoundary(string channelId)
        {
            Assert.That(GazeSourceIdConvention.IsValidChannelId(channelId), Is.False);
        }

        [Test]
        public void IsValidChannelId_RejectsMoreThanMaximumLength()
        {
            Assert.That(GazeSourceIdConvention.IsValidChannelId(new string('a', 65)), Is.False);
        }

        [Test]
        public void IsValidChannelId_AcceptsMaximumLength()
        {
            Assert.That(GazeSourceIdConvention.IsValidChannelId(new string('a', 64)), Is.True);
        }
    }
}
