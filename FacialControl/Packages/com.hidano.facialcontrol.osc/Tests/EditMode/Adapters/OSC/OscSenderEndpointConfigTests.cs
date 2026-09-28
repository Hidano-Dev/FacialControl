using System;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters
{
    [TestFixture]
    [SmallTest]
    public class OscSenderEndpointConfigTests : SizedTestFixture
    {
        [Test]
        public void Type_SerializableAttribute_IsDefined()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(OscSenderEndpointConfig), typeof(SerializableAttribute)));
        }

        [Test]
        public void Constructor_DefaultValues_AreEnabledVrchatLocalEndpoint()
        {
            var config = new OscSenderEndpointConfig();

            Assert.AreEqual(OscSenderEndpointConfig.DefaultEndpoint, config.endpoint);
            Assert.AreEqual(OscConfiguration.DefaultSendPort, config.port);
            Assert.IsTrue(config.enabled);
            Assert.AreEqual(AddressPresetKind.VRChat, config.preset);
        }

        [Test]
        public void Constructor_CustomValues_StoresEndpointPortEnabledAndPreset()
        {
            var config = new OscSenderEndpointConfig(
                "renderer.local",
                9012,
                false,
                AddressPresetKind.ARKit);

            Assert.AreEqual("renderer.local", config.endpoint);
            Assert.AreEqual(9012, config.port);
            Assert.IsFalse(config.enabled);
            Assert.AreEqual(AddressPresetKind.ARKit, config.preset);
        }
    }
}
