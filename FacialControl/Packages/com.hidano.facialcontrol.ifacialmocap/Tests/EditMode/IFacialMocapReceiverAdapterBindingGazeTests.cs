using System.Linq;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    [SmallTest]
    public sealed class IFacialMocapReceiverAdapterBindingGazeTests : SizedTestFixture
    {
        [Test]
        public void GazeSourceDeclarations_AdvertiseDefaultChannelAsLeftRightPair()
        {
            var binding = new IFacialMocapReceiverAdapterBinding();

            var declarations = ((IGazeSourceProvider)binding).GetGazeSourceDeclarations().ToArray();

            Assert.That(declarations, Has.Length.EqualTo(1));
            Assert.That(declarations[0].ChannelId, Is.EqualTo(GazeSourceIdConvention.DefaultChannelId));
            Assert.That(declarations[0].ProvidesLeftRightPair, Is.True);
        }

        [Test]
        public void DefaultGazeSourceIds_PreserveIFacialMocapRegistrationConvention()
        {
            Assert.That(
                GazeSourceIdConvention.Compose("ifm", GazeSourceIdConvention.DefaultChannelId, GazeSide.Left),
                Is.EqualTo("ifm:gaze.left"));
            Assert.That(
                GazeSourceIdConvention.Compose("ifm", GazeSourceIdConvention.DefaultChannelId, GazeSide.Right),
                Is.EqualTo("ifm:gaze.right"));
        }
    }
}
