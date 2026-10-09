using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public sealed class RuntimeMappingResolverTests : SizedTestFixture
    {
        [Test]
        public void ResolveInitialMappings_ManualBlendShapeEntriesOnly_ReturnsManualOriginsInInputOrder()
        {
            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.ResolveInitialMappings(
                new[]
                {
                    BlendShapeEntry("Smile", "/manual/smile"),
                    new OscMappingEntry
                    {
                        mode = OscMappingMode.Gaze_VRChat_XY,
                        expressionId = "Gaze_VRChat_XY",
                        addressPattern = "/avatar/parameters/Gaze"
                    },
                    BlendShapeEntry("Angry", "/manual/angry"),
                    BlendShapeEntry(string.Empty, "/manual/empty"),
                    BlendShapeEntry("NoAddress", string.Empty)
                });

            Assert.AreEqual(2, result.RuntimeMappings.Length);
            Assert.AreEqual(2, result.ManualCount);
            AssertMapping(result, 0, "Smile", "/manual/smile", OscReceiverAdapterBinding.MappingOrigin.Manual);
            AssertMapping(result, 1, "Angry", "/manual/angry", OscReceiverAdapterBinding.MappingOrigin.Manual);
        }

        [Test]
        public void ResolveInitialMappings_EmptyEntries_ReturnsEmptyArrays()
        {
            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.ResolveInitialMappings(null);

            Assert.IsNotNull(result.RuntimeMappings);
            Assert.IsNotNull(result.Origins);
            Assert.AreEqual(0, result.RuntimeMappings.Length);
            Assert.AreEqual(0, result.Origins.Length);
        }

        private static OscMappingEntry BlendShapeEntry(string expressionId, string addressPattern)
        {
            return new OscMappingEntry
            {
                mode = OscMappingMode.Normal_BlendShape,
                expressionId = expressionId,
                addressPattern = addressPattern
            };
        }

        private static void AssertMapping(
            RuntimeMappingResolver.ResolveResult result,
            int index,
            string blendShapeName,
            string address,
            OscReceiverAdapterBinding.MappingOrigin origin)
        {
            Assert.AreEqual(blendShapeName, result.RuntimeMappings[index].BlendShapeName);
            Assert.AreEqual(address, result.RuntimeMappings[index].OscAddress);
            Assert.AreEqual(string.Empty, result.RuntimeMappings[index].Layer);
            Assert.AreEqual(origin, result.Origins[index]);
        }
    }
}
