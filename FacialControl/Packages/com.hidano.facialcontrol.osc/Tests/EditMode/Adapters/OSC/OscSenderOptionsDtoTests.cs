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
                    new OscSenderEndpointDto("127.0.0.1", 9000, true),
                    new OscSenderEndpointDto("renderer.local", 9012, false)
                },
                blendShapeMapping = new[]
                {
                    "Joy",
                    "Blink_L"
                },
                suppressLoopback = false,
                layoutRefreshIntervalSeconds = 2.5f
            };

            string json = source.ToJson();
            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson(json);

            Assert.AreEqual(2, result.endpoints.Length);
            Assert.AreEqual("127.0.0.1", result.endpoints[0].ip);
            Assert.AreEqual(9000, result.endpoints[0].port);
            Assert.IsTrue(result.endpoints[0].enabled);
            Assert.AreEqual("renderer.local", result.endpoints[1].ip);
            Assert.AreEqual(9012, result.endpoints[1].port);
            Assert.IsFalse(result.endpoints[1].enabled);
            Assert.AreEqual(source.blendShapeMapping, result.blendShapeMapping);
            Assert.IsFalse(result.suppressLoopback);
            Assert.AreEqual(2.5f, result.layoutRefreshIntervalSeconds);
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
                "\"layoutRefreshIntervalSeconds\":4.0" +
                "}";

            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson(Json);

            Assert.AreEqual(1, result.endpoints.Length);
            Assert.AreEqual("127.0.0.1", result.endpoints[0].ip);
            Assert.AreEqual(9100, result.endpoints[0].port);
            Assert.IsTrue(result.endpoints[0].enabled);
            Assert.AreEqual(new[] { "Smile" }, result.blendShapeMapping);
            Assert.IsTrue(result.suppressLoopback);
            Assert.AreEqual(4.0f, result.layoutRefreshIntervalSeconds);
        }

        [Test]
        public void FromJson_LegacyHeartbeatIntervalKey_ReadsAsLayoutRefreshInterval()
        {
            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson("{\"heartbeatIntervalSeconds\":3.5}");

            Assert.AreEqual(3.5f, result.layoutRefreshIntervalSeconds);
            StringAssert.DoesNotContain("heartbeatIntervalSeconds", result.ToJson());
        }

        [Test]
        public void FromJson_MissingRequiredKeys_UsesDefaults()
        {
            OscSenderOptionsDto result = OscSenderOptionsDto.FromJson("{}");

            Assert.IsNotNull(result.endpoints);
            Assert.AreEqual(1, result.endpoints.Length);
            Assert.AreEqual(OscSenderEndpointConfig.DefaultEndpoint, result.endpoints[0].ip);
            Assert.AreEqual(OscConfiguration.DefaultSendPort, result.endpoints[0].port);
            Assert.IsTrue(result.endpoints[0].enabled);
            Assert.IsNotNull(result.blendShapeMapping);
            Assert.IsEmpty(result.blendShapeMapping);
            Assert.IsTrue(result.suppressLoopback);
            Assert.AreEqual(OscSenderOptionsDto.DefaultLayoutRefreshIntervalSeconds, result.layoutRefreshIntervalSeconds);
        }

        [Test]
        public void ToConfig_CopiesEndpointPortAndEnabled()
        {
            var dto = new OscSenderEndpointDto(" 127.0.0.1 ", 9100, true);

            OscSenderEndpointConfig config = dto.ToConfig();

            Assert.AreEqual("127.0.0.1", config.endpoint);
            Assert.AreEqual(9100, config.port);
            Assert.IsTrue(config.enabled);
        }

        [Test]
        public void ToJson_DoesNotWritePresetKeys()
        {
            string json = new OscSenderOptionsDto().ToJson();

            StringAssert.DoesNotContain("\"preset\"", json);
            StringAssert.DoesNotContain("\"sendPreset\"", json);
        }
    }
}
