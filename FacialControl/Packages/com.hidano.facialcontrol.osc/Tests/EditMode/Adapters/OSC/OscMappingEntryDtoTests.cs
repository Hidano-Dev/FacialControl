using System;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.Json.Dto;
using Hidano.FacialControl.Adapters.OSC;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.Json
{
    [TestFixture]
    [SmallTest]
    public sealed class OscMappingEntryDtoTests : SizedTestFixture
    {
        [Test]
        public void Type_SerializableAttribute_IsDefined()
        {
            Assert.IsTrue(Attribute.IsDefined(typeof(OscMappingEntryDto), typeof(SerializableAttribute)));
        }

        [TestCase(OscMappingEntryDto.ModeBlendShape, OscMappingMode.Normal_BlendShape)]
        [TestCase(OscMappingEntryDto.ModeGazeVrchatXy, OscMappingMode.Gaze_VRChat_XY)]
        [TestCase(OscMappingEntryDto.ModeGazeArkit8Bs, OscMappingMode.Gaze_ARKit_8BS)]
        public void ToMappingEntry_ModeString_ConvertsToRuntimeMode(string mode, OscMappingMode expected)
        {
            var dto = new OscMappingEntryDto
            {
                mode = mode,
                expressionId = "Eyes",
                addressPattern = expected == OscMappingMode.Gaze_ARKit_8BS
                    ? string.Empty
                    : "/avatar/parameters/Eyes"
            };

            bool converted = dto.TryToMappingEntry(out OscMappingEntry entry);

            Assert.IsTrue(converted);
            Assert.AreEqual(expected, entry.mode);
            Assert.AreEqual("Eyes", entry.expressionId);
        }

        [Test]
        public void JsonRoundTrip_GazeVrchatXy_PreservesBaseAddressAndSourceIds()
        {
            var source = new OscMappingEntryDto
            {
                mode = OscMappingEntryDto.ModeGazeVrchatXy,
                expressionId = "Look",
                addressPattern = "/avatar/parameters/Look",
                sourceIdLeft = "Look.left",
                sourceIdRight = "Look.right",
                leftRightIndependent = true
            };

            string json = source.ToJson();
            OscMappingEntryDto result = OscMappingEntryDto.FromJson(json);

            Assert.IsNotNull(result);
            Assert.AreEqual(OscMappingEntryDto.ModeGazeVrchatXy, result.mode);
            Assert.AreEqual("Look", result.expressionId);
            Assert.AreEqual("/avatar/parameters/Look", result.addressPattern);
            Assert.AreEqual("Look.left", result.sourceIdLeft);
            Assert.AreEqual("Look.right", result.sourceIdRight);
            Assert.IsTrue(result.leftRightIndependent);
        }

        [Test]
        public void FromJson_LeftRightIndependentGazeMissingOneSource_SkipsEntryAndLogsWarning()
        {
            const string Json =
                "{" +
                "\"mode\":\"gazeVrchatXy\"," +
                "\"expressionId\":\"Look\"," +
                "\"addressPattern\":\"/avatar/parameters/Look\"," +
                "\"sourceIdLeft\":\"Look.left\"," +
                "\"leftRightIndependent\":true" +
                "}";

            LogAssert.Expect(LogType.Warning, new Regex("sourceIdLeft/sourceIdRight"));

            OscMappingEntryDto result = OscMappingEntryDto.FromJson(Json);

            Assert.IsNull(result);
        }

        [Test]
        public void FromJson_ArKitAddressPattern_IgnoresAddressPatternAndLogsInfo()
        {
            const string Json =
                "{" +
                "\"mode\":\"gazeArkit8Bs\"," +
                "\"expressionId\":\"Look\"," +
                "\"addressPattern\":\"/ignored/address\"" +
                "}";

            LogAssert.Expect(LogType.Log, new Regex("ignored"));

            OscMappingEntryDto result = OscMappingEntryDto.FromJson(Json);

            Assert.IsNotNull(result);
            Assert.AreEqual(OscMappingEntryDto.ModeGazeArkit8Bs, result.mode);
            Assert.AreEqual("Look", result.expressionId);
            Assert.AreEqual(string.Empty, result.addressPattern);
            Assert.IsTrue(result.TryToMappingEntry(out OscMappingEntry entry));
            Assert.AreEqual(OscMappingMode.Gaze_ARKit_8BS, entry.mode);
            Assert.AreEqual(string.Empty, entry.addressPattern);
        }
    }
}
