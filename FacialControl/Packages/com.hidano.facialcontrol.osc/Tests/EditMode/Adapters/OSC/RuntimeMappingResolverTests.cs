using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Services;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

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
            Assert.AreEqual(0, result.HeartbeatAutoCount);
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

        [Test]
        public void MergeWithHeartbeat_PartialManualMappings_AppendsHeartbeatDiffAfterManualMappings()
        {
            bool warnedEmptyIntersection = false;
            bool warnedAddressCollision = false;

            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.MergeWithHeartbeat(
                new[] { BlendShapeEntry("Smile", "/manual/smile") },
                new[] { "Smile", "Angry", "Blink" },
                new[] { "Smile", "Angry", "Blink", "UnusedMeshOnly" },
                AddressPresetKind.VRChat,
                null,
                ref warnedEmptyIntersection,
                ref warnedAddressCollision);

            Assert.AreEqual(3, result.RuntimeMappings.Length);
            Assert.AreEqual(1, result.ManualCount);
            Assert.AreEqual(2, result.HeartbeatAutoCount);
            AssertMapping(result, 0, "Smile", "/manual/smile", OscReceiverAdapterBinding.MappingOrigin.Manual);
            AssertMapping(result, 1, "Angry", "/avatar/parameters/Angry", OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto);
            AssertMapping(result, 2, "Blink", "/avatar/parameters/Blink", OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto);
        }

        [Test]
        public void MergeWithHeartbeat_ManualAndHeartbeatDuplicateExpressionId_KeepsManualAddress()
        {
            bool warnedEmptyIntersection = false;
            bool warnedAddressCollision = false;

            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.MergeWithHeartbeat(
                new[] { BlendShapeEntry("Smile", "/manual/smile") },
                new[] { "Smile" },
                new[] { "Smile" },
                AddressPresetKind.VRChat,
                null,
                ref warnedEmptyIntersection,
                ref warnedAddressCollision);

            Assert.AreEqual(1, result.RuntimeMappings.Length);
            Assert.AreEqual(1, result.ManualCount);
            Assert.AreEqual(0, result.HeartbeatAutoCount);
            AssertMapping(result, 0, "Smile", "/manual/smile", OscReceiverAdapterBinding.MappingOrigin.Manual);
        }

        [Test]
        public void MergeWithHeartbeat_ArKitPreset_UsesArKitForStandardNamesAndVrChatForNonStandardNames()
        {
            bool warnedEmptyIntersection = false;
            bool warnedAddressCollision = false;
            string arkitName = ARKitDetector.ARKit52Names[0];

            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.MergeWithHeartbeat(
                null,
                new[] { arkitName, "CustomSmile" },
                new[] { arkitName, "CustomSmile" },
                AddressPresetKind.ARKit,
                null,
                ref warnedEmptyIntersection,
                ref warnedAddressCollision);

            AssertMapping(result, 0, arkitName, "/ARKit/" + arkitName, OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto);
            AssertMapping(result, 1, "CustomSmile", "/avatar/parameters/CustomSmile", OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto);
        }

        [Test]
        public void MergeWithHeartbeat_CustomPreset_UsesCustomPrefix()
        {
            bool warnedEmptyIntersection = false;
            bool warnedAddressCollision = false;

            RuntimeMappingResolver.ResolveResult result = RuntimeMappingResolver.MergeWithHeartbeat(
                null,
                new[] { "Smile" },
                new[] { "Smile" },
                AddressPresetKind.Custom,
                "/custom/",
                ref warnedEmptyIntersection,
                ref warnedAddressCollision);

            AssertMapping(result, 0, "Smile", "/custom/Smile", OscReceiverAdapterBinding.MappingOrigin.HeartbeatAuto);
        }

        [Test]
        public void MergeWithHeartbeat_EmptyIntersection_WarnsOnceAndReturnsManualOnly()
        {
            bool warnedEmptyIntersection = false;
            bool warnedAddressCollision = false;
            LogAssert.Expect(LogType.Warning, new Regex("heartbeat.*mesh.*intersection.*empty"));

            RuntimeMappingResolver.ResolveResult first = RuntimeMappingResolver.MergeWithHeartbeat(
                new[] { BlendShapeEntry("ManualOnly", "/manual") },
                new[] { "SenderOnly" },
                new[] { "MeshOnly" },
                AddressPresetKind.VRChat,
                null,
                ref warnedEmptyIntersection,
                ref warnedAddressCollision);
            RuntimeMappingResolver.ResolveResult second = RuntimeMappingResolver.MergeWithHeartbeat(
                new[] { BlendShapeEntry("ManualOnly", "/manual") },
                new[] { "SenderOnly" },
                new[] { "MeshOnly" },
                AddressPresetKind.VRChat,
                null,
                ref warnedEmptyIntersection,
                ref warnedAddressCollision);

            Assert.AreEqual(1, first.RuntimeMappings.Length);
            Assert.AreEqual(1, first.ManualCount);
            Assert.AreEqual(0, first.HeartbeatAutoCount);
            Assert.AreEqual(1, second.RuntimeMappings.Length);
            Assert.IsTrue(warnedEmptyIntersection);
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
