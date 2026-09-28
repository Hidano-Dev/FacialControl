using System;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    [TestFixture]
    [SmallTest]
    public sealed class OscSenderOptionsDtoTests : SizedTestFixture
    {
        [Test]
        public void Type_SerializableAttribute_IsDefined()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(OscSenderOptionsDto), typeof(SerializableAttribute)));
            Assert.IsTrue(Attribute.IsDefined(typeof(OscSenderEndpointDto), typeof(SerializableAttribute)));
        }

        [Test]
        public void JsonRoundTrip_PreservesValues()
        {
            var source = new OscSenderOptionsDto
            {
                endpoints = new[]
                {
                    new OscSenderEndpointDto("127.0.0.1", 9000, OscSenderEndpointDto.PresetVRChat, true),
                    new OscSenderEndpointDto("renderer.local", 9012, OscSenderEndpointDto.PresetARKit, false)
                },
                blendShapeMapping = new[]
                {
                    "Joy",
                    "Blink_L"
                },
                sendPreset = false,
                suppressLoopback = false,
                heartbeatIntervalSeconds = 2.5f
            };

            string json = source.ToJson();
            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson(json);

            Assert.AreEqual(2, result.endpoints.Length);
            Assert.AreEqual("127.0.0.1", result.endpoints[0].ip);
            Assert.AreEqual(9000, result.endpoints[0].port);
            Assert.AreEqual(OscSenderEndpointDto.PresetVRChat, result.endpoints[0].preset);
            Assert.IsTrue(result.endpoints[0].enabled);
            Assert.AreEqual("renderer.local", result.endpoints[1].ip);
            Assert.AreEqual(9012, result.endpoints[1].port);
            Assert.AreEqual(OscSenderEndpointDto.PresetARKit, result.endpoints[1].preset);
            Assert.IsFalse(result.endpoints[1].enabled);
            Assert.AreEqual(source.blendShapeMapping, result.blendShapeMapping);
            Assert.IsFalse(result.sendPreset);
            Assert.IsFalse(result.suppressLoopback);
            Assert.AreEqual(2.5f, result.heartbeatIntervalSeconds);
        }

        [Test]
        public void FromJson_UnknownKeys_IgnoresUnknownKeys()
        {
            const string Json =
                "{" +
                "\"unknownRoot\":123," +
                "\"endpoints\":[{\"ip\":\"127.0.0.1\",\"port\":9100,\"preset\":\"arkit\",\"enabled\":true,\"extra\":\"ignored\"}]," +
                "\"blendShapeMapping\":[\"Smile\"]," +
                "\"gazeExpressionIds\":[\"Eyes\"]," +
                "\"sendPreset\":false," +
                "\"suppressLoopback\":true," +
                "\"heartbeatIntervalSeconds\":4.0" +
                "}";

            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson(Json);

            Assert.AreEqual(1, result.endpoints.Length);
            Assert.AreEqual("127.0.0.1", result.endpoints[0].ip);
            Assert.AreEqual(9100, result.endpoints[0].port);
            Assert.AreEqual(OscSenderEndpointDto.PresetARKit, result.endpoints[0].preset);
            Assert.IsTrue(result.endpoints[0].enabled);
            Assert.AreEqual(new[] { "Smile" }, result.blendShapeMapping);
            Assert.IsFalse(result.sendPreset);
            Assert.IsTrue(result.suppressLoopback);
            Assert.AreEqual(4.0f, result.heartbeatIntervalSeconds);
        }

        [Test]
        public void FromJson_MissingRequiredKeys_UsesDefaults()
        {
            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson("{}");

            Assert.IsNotNull(result.endpoints);
            Assert.AreEqual(1, result.endpoints.Length);
            Assert.AreEqual(OscSenderEndpointConfig.DefaultEndpoint, result.endpoints[0].ip);
            Assert.AreEqual(OscConfiguration.DefaultSendPort, result.endpoints[0].port);
            Assert.AreEqual(OscSenderEndpointDto.PresetVRChat, result.endpoints[0].preset);
            Assert.IsTrue(result.endpoints[0].enabled);
            Assert.IsNotNull(result.blendShapeMapping);
            Assert.IsEmpty(result.blendShapeMapping);
            Assert.IsTrue(result.sendPreset);
            Assert.IsTrue(result.suppressLoopback);
            Assert.AreEqual(OscSenderOptionsDto.DefaultHeartbeatIntervalSeconds, result.heartbeatIntervalSeconds);
        }

        [Test]
        public void ToConfig_ConvertsPresetStringToAddressPresetKind()
        {
            var dto = new OscSenderEndpointDto("127.0.0.1", 9100, "ARKit", true);

            OscSenderEndpointConfig config = dto.ToConfig();

            Assert.AreEqual("127.0.0.1", config.endpoint);
            Assert.AreEqual(9100, config.port);
            Assert.IsTrue(config.enabled);
            Assert.AreEqual(AddressPresetKind.ARKit, config.preset);
        }
    }
}
