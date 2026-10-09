using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="OscReceiverRuntimeSettingsSO"/>（OSC 受信の上級設定）の既定値・JSON 往復・不正値補正・
    /// 旧 <see cref="OscRuntimeSettingsSO"/> からの値の写しを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OscReceiverRuntimeSettingsSOTests : SizedTestFixture
    {
        private OscReceiverRuntimeSettingsSO _instance;

        [SetUp]
        public void SetUp()
        {
            _instance = ScriptableObject.CreateInstance<OscReceiverRuntimeSettingsSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                Object.DestroyImmediate(_instance);
                _instance = null;
            }
        }

        [Test]
        public void Defaults_OnFreshInstance_ReturnsExpectedValuesAndIsDefault()
        {
            Assert.AreEqual(0f, _instance.StalenessSeconds);
            Assert.AreEqual(FailSafeMode.RevertToBase, _instance.FailSafeMode);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _instance.BundleMode);
            Assert.AreEqual(
                OscReceiverRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
            Assert.IsTrue(_instance.IsDefault);
        }

        [Test]
        public void ToJson_ThenFromJson_RoundTripsAllFields()
        {
            _instance.FromJson(
                "{\"label\":\"studio\",\"stalenessSeconds\":0.5,\"failSafeMode\":\"holdLastValue\","
                + "\"bundleMode\":\"individualMessage\",\"bundleAccumulationTimeoutMs\":12.0}");

            string json = _instance.ToJson();
            var restored = ScriptableObject.CreateInstance<OscReceiverRuntimeSettingsSO>();
            try
            {
                restored.FromJson(json);

                Assert.AreEqual("studio", restored.Label);
                Assert.AreEqual(0.5f, restored.StalenessSeconds);
                Assert.AreEqual(FailSafeMode.HoldLastValue, restored.FailSafeMode);
                Assert.AreEqual(BundleInterpretationMode.IndividualMessage, restored.BundleMode);
                Assert.AreEqual(12f, restored.BundleAccumulationTimeoutMs);
                Assert.IsFalse(restored.IsDefault);
            }
            finally
            {
                Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void ToJson_DoesNotContainListenEndpointPortEnabledOrConsistencyCheck()
        {
            string json = _instance.ToJson();

            StringAssert.DoesNotContain("listenEndpoint", json);
            StringAssert.DoesNotContain("listenPort", json);
            StringAssert.DoesNotContain("receiverEnabled", json);
            StringAssert.DoesNotContain("consistencyCheckWarnLog", json);
        }

        [Test]
        public void FromJson_InvalidValues_NormalizesToDefaults()
        {
            _instance.FromJson("{\"stalenessSeconds\":-1.0,\"bundleAccumulationTimeoutMs\":0.0}");

            Assert.AreEqual(0f, _instance.StalenessSeconds);
            Assert.AreEqual(
                OscReceiverRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void CreateFromLegacy_CopiesAdvancedReceiverValues()
        {
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            OscReceiverRuntimeSettingsSO created = null;
            try
            {
                legacy.FromJson(
                    "{\"label\":\"old\",\"listenPort\":9100,\"stalenessSeconds\":1.0,\"failSafeMode\":\"holdLastValue\","
                    + "\"consistencyCheckWarnLog\":false,\"bundleMode\":\"individualMessage\",\"bundleAccumulationTimeoutMs\":8.0}");

                created = OscReceiverRuntimeSettingsSO.CreateFromLegacy(legacy);

                Assert.AreEqual("old", created.Label);
                Assert.AreEqual(1f, created.StalenessSeconds);
                Assert.AreEqual(FailSafeMode.HoldLastValue, created.FailSafeMode);
                Assert.AreEqual(BundleInterpretationMode.IndividualMessage, created.BundleMode);
                Assert.AreEqual(8f, created.BundleAccumulationTimeoutMs);
            }
            finally
            {
                Object.DestroyImmediate(legacy);
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }
        }

        [Test]
        public void CreateFromLegacy_LegacyWithDefaultAdvancedValues_IsDefault()
        {
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            OscReceiverRuntimeSettingsSO created = null;
            try
            {
                // ポートだけ変えた旧設定は、上級設定としては既定値のまま。
                legacy.FromJson("{\"listenPort\":9100}");

                created = OscReceiverRuntimeSettingsSO.CreateFromLegacy(legacy);

                Assert.IsTrue(created.IsDefault);
            }
            finally
            {
                Object.DestroyImmediate(legacy);
                if (created != null)
                {
                    Object.DestroyImmediate(created);
                }
            }
        }
    }
}
