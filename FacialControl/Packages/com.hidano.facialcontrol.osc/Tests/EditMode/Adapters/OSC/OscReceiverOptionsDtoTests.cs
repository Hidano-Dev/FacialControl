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
    public sealed class OscReceiverOptionsDtoTests : SizedTestFixture
    {
        [Test]
        public void Type_SerializableAttribute_IsDefined()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(OscReceiverOptionsDto), typeof(SerializableAttribute)));
        }

        [Test]
        public void JsonRoundTrip_ThreeModes_PreservesValues()
        {
            var source = new OscReceiverOptionsDto
            {
                listenEndpoint = "0.0.0.0",
                listenPort = 9100,
                mappings = new[]
                {
                    new OscMappingEntryDto
                    {
                        mode = OscMappingEntryDto.ModeBlendShape,
                        expressionId = "Smile",
                        addressPattern = "/avatar/parameters/Smile"
                    },
                    new OscMappingEntryDto
                    {
                        mode = OscMappingEntryDto.ModeGazeVrchatXy,
                        expressionId = "Eyes",
                        addressPattern = "/avatar/parameters/Eyes",
                        sourceIdLeft = "Eyes.left",
                        sourceIdRight = "Eyes.right",
                        leftRightIndependent = true
                    },
                    new OscMappingEntryDto
                    {
                        mode = OscMappingEntryDto.ModeGazeArkit8Bs,
                        expressionId = "EyesArKit"
                    }
                },
                stalenessSeconds = 0.25f,
                failSafeMode = OscReceiverOptionsDto.FailSafeHoldLastValue,
                consistencyCheckWarnLog = false,
                bundleMode = OscReceiverOptionsDto.BundleIndividualMessage,
                bundleAccumulationTimeoutMs = 12f
            };

            string json = source.ToJson();
            OscReceiverOptionsDto result = OscReceiverOptionsDto.FromJson(json);

            Assert.AreEqual("0.0.0.0", result.listenEndpoint);
            Assert.AreEqual(9100, result.listenPort);
            Assert.AreEqual(3, result.mappings.Length);
            Assert.AreEqual(OscMappingEntryDto.ModeBlendShape, result.mappings[0].mode);
            Assert.AreEqual("Smile", result.mappings[0].expressionId);
            Assert.AreEqual("/avatar/parameters/Smile", result.mappings[0].addressPattern);
            Assert.AreEqual(OscMappingEntryDto.ModeGazeVrchatXy, result.mappings[1].mode);
            Assert.AreEqual("Eyes", result.mappings[1].expressionId);
            Assert.AreEqual("/avatar/parameters/Eyes", result.mappings[1].addressPattern);
            Assert.AreEqual("Eyes.left", result.mappings[1].sourceIdLeft);
            Assert.AreEqual("Eyes.right", result.mappings[1].sourceIdRight);
            Assert.IsTrue(result.mappings[1].leftRightIndependent);
            Assert.AreEqual(OscMappingEntryDto.ModeGazeArkit8Bs, result.mappings[2].mode);
            Assert.AreEqual("EyesArKit", result.mappings[2].expressionId);
            Assert.AreEqual(string.Empty, result.mappings[2].addressPattern);
            Assert.AreEqual(0.25f, result.stalenessSeconds);
            Assert.AreEqual(OscReceiverOptionsDto.FailSafeHoldLastValue, result.failSafeMode);
            Assert.IsFalse(result.consistencyCheckWarnLog);
            Assert.AreEqual(OscReceiverOptionsDto.BundleIndividualMessage, result.bundleMode);
            Assert.AreEqual(12f, result.bundleAccumulationTimeoutMs);
        }

        [Test]
        public void FromJson_UnknownKeys_IgnoresUnknownKeys()
        {
            const string Json =
                "{" +
                "\"unknownRoot\":123," +
                "\"listenEndpoint\":\"127.0.0.1\"," +
                "\"listenPort\":9200," +
                "\"mappings\":[{" +
                "\"mode\":\"blendShape\"," +
                "\"expressionId\":\"Blink_L\"," +
                "\"addressPattern\":\"/avatar/parameters/Blink_L\"," +
                "\"unknownMapping\":true" +
                "}]," +
                "\"stalenessSeconds\":1.5," +
                "\"failSafeMode\":\"revertToBase\"," +
                "\"consistencyCheckWarnLog\":true," +
                "\"bundleMode\":\"atomicSwap\"," +
                "\"bundleAccumulationTimeoutMs\":9.0" +
                "}";

            OscReceiverOptionsDto result = OscReceiverOptionsDto.FromJson(Json);

            Assert.AreEqual("127.0.0.1", result.listenEndpoint);
            Assert.AreEqual(9200, result.listenPort);
            Assert.AreEqual(1, result.mappings.Length);
            Assert.AreEqual("Blink_L", result.mappings[0].expressionId);
            Assert.AreEqual("/avatar/parameters/Blink_L", result.mappings[0].addressPattern);
            Assert.AreEqual(1.5f, result.stalenessSeconds);
            Assert.AreEqual(OscReceiverOptionsDto.FailSafeRevertToBase, result.failSafeMode);
            Assert.IsTrue(result.consistencyCheckWarnLog);
            Assert.AreEqual(OscReceiverOptionsDto.BundleAtomicSwap, result.bundleMode);
            Assert.AreEqual(9f, result.bundleAccumulationTimeoutMs);
        }

        [Test]
        public void FromJson_MissingRequiredKeys_UsesDefaults()
        {
            OscReceiverOptionsDto result = OscReceiverOptionsDto.FromJson("{}");

            Assert.AreEqual(OscReceiverOptionsDto.DefaultListenEndpoint, result.listenEndpoint);
            Assert.AreEqual(OscConfiguration.DefaultReceivePort, result.listenPort);
            Assert.IsNotNull(result.mappings);
            Assert.IsEmpty(result.mappings);
            Assert.AreEqual(OscReceiverOptionsDto.DefaultStalenessSeconds, result.stalenessSeconds);
            Assert.AreEqual(OscReceiverOptionsDto.FailSafeRevertToBase, result.failSafeMode);
            Assert.IsTrue(result.consistencyCheckWarnLog);
            Assert.AreEqual(OscReceiverOptionsDto.BundleAtomicSwap, result.bundleMode);
            Assert.AreEqual(OscReceiverOptionsDto.DefaultBundleAccumulationTimeoutMs, result.bundleAccumulationTimeoutMs);
        }

        [Test]
        public void ToMappingEntries_ConvertsStringsToRuntimeEnums()
        {
            var dto = new OscReceiverOptionsDto
            {
                mappings = new[]
                {
                    new OscMappingEntryDto
                    {
                        mode = "gazeVrchatXy",
                        expressionId = "Eyes",
                        addressPattern = "/avatar/parameters/Eyes"
                    }
                },
                failSafeMode = "holdLastValue",
                bundleMode = "individualMessage"
            };

            OscMappingEntry[] mappings = dto.ToMappingEntries();

            Assert.AreEqual(1, mappings.Length);
            Assert.AreEqual(OscMappingMode.Gaze_VRChat_XY, mappings[0].mode);
            Assert.AreEqual(FailSafeMode.HoldLastValue, dto.ToFailSafeMode());
            Assert.AreEqual(BundleInterpretationMode.IndividualMessage, dto.ToBundleInterpretationMode());
        }
    }
}
