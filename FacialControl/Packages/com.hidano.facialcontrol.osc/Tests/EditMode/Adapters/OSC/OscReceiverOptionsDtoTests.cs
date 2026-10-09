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
        public void JsonRoundTrip_AllKeys_PreservesValues()
        {
            var source = new OscReceiverOptionsDto
            {
                listenPort = 9100,
                stalenessSeconds = 0.25f,
                failSafeMode = OscReceiverOptionsDto.FailSafeHoldLastValue,
                bundleMode = OscReceiverOptionsDto.BundleIndividualMessage,
                bundleAccumulationTimeoutMs = 12f
            };

            string json = source.ToJson();
            OscReceiverOptionsDto result = OscReceiverOptionsDto.FromJson(json);

            StringAssert.DoesNotContain("listenEndpoint", json,
                "受信 IP は廃止したため JSON に書き出さない。");
            StringAssert.DoesNotContain("mappings", json,
                "手動のアドレス mapping は廃止したため JSON に書き出さない。");
            StringAssert.DoesNotContain("consistencyCheckWarnLog", json,
                "heartbeat の整合チェックは廃止したため JSON に書き出さない。");
            Assert.AreEqual(9100, result.listenPort);
            Assert.AreEqual(0.25f, result.stalenessSeconds);
            Assert.AreEqual(OscReceiverOptionsDto.FailSafeHoldLastValue, result.failSafeMode);
            Assert.AreEqual(OscReceiverOptionsDto.BundleIndividualMessage, result.bundleMode);
            Assert.AreEqual(12f, result.bundleAccumulationTimeoutMs);
        }

        [Test]
        public void FromJson_UnknownKeys_IgnoresUnknownKeys()
        {
            // listenEndpoint・mappings・consistencyCheckWarnLog は廃止した旧キー。旧形式の JSON を読んでも他の値は壊れない。
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

            Assert.AreEqual(9200, result.listenPort);
            Assert.AreEqual(1.5f, result.stalenessSeconds);
            Assert.AreEqual(OscReceiverOptionsDto.FailSafeRevertToBase, result.failSafeMode);
            Assert.AreEqual(OscReceiverOptionsDto.BundleAtomicSwap, result.bundleMode);
            Assert.AreEqual(9f, result.bundleAccumulationTimeoutMs);
        }

        [Test]
        public void FromJson_MissingRequiredKeys_UsesDefaults()
        {
            OscReceiverOptionsDto result = OscReceiverOptionsDto.FromJson("{}");

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, result.listenPort);
            Assert.AreEqual(OscReceiverOptionsDto.DefaultStalenessSeconds, result.stalenessSeconds);
            Assert.AreEqual(OscReceiverOptionsDto.FailSafeRevertToBase, result.failSafeMode);
            Assert.AreEqual(OscReceiverOptionsDto.BundleAtomicSwap, result.bundleMode);
            Assert.AreEqual(OscReceiverOptionsDto.DefaultBundleAccumulationTimeoutMs, result.bundleAccumulationTimeoutMs);
        }

        [Test]
        public void ToRuntimeEnums_ConvertsStrings()
        {
            var dto = new OscReceiverOptionsDto
            {
                failSafeMode = "holdLastValue",
                bundleMode = "individualMessage"
            };

            Assert.AreEqual(FailSafeMode.HoldLastValue, dto.ToFailSafeMode());
            Assert.AreEqual(BundleInterpretationMode.IndividualMessage, dto.ToBundleInterpretationMode());
        }
    }
}
