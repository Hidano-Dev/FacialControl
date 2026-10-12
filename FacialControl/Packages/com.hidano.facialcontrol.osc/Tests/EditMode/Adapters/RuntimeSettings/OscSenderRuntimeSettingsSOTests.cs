using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="OscSenderRuntimeSettingsSO"/>（OSC 送信の上級設定）の既定値・JSON 往復・旧キーの読み飛ばし・
    /// 旧 <see cref="OscRuntimeSettingsSO"/> からの値の写しを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OscSenderRuntimeSettingsSOTests : SizedTestFixture
    {
        private OscSenderRuntimeSettingsSO _instance;

        [SetUp]
        public void SetUp()
        {
            _instance = ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
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
            Assert.IsTrue(_instance.SuppressLoopback);
            Assert.IsTrue(_instance.IsDefault);
        }

        [Test]
        public void ToJson_ThenFromJson_RoundTripsAllFields()
        {
            _instance.FromJson("{\"label\":\"studio\",\"suppressLoopback\":false}");

            string json = _instance.ToJson();
            var restored = ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
            try
            {
                restored.FromJson(json);

                Assert.AreEqual("studio", restored.Label);
                Assert.IsFalse(restored.SuppressLoopback);
                Assert.IsFalse(restored.IsDefault);
            }
            finally
            {
                Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void ToJson_DoesNotContainEndpointsOrEnabled()
        {
            string json = _instance.ToJson();

            StringAssert.DoesNotContain("endpoints", json);
            StringAssert.DoesNotContain("senderEnabled", json);
        }

        [Test]
        public void FromJson_LegacyIntervalKeys_AreSkippedAndNotWrittenBack()
        {
            // 効果の無い更新間隔は HID-196 で削除した。旧キーが残っていても読み飛ばし、書き出さない。
            _instance.FromJson("{\"label\":\"studio\",\"heartbeatIntervalSeconds\":2.5,\"layoutRefreshIntervalSeconds\":7.0}");

            string json = _instance.ToJson();
            Assert.AreEqual("studio", _instance.Label);
            Assert.IsTrue(_instance.IsDefault);
            StringAssert.DoesNotContain("heartbeatIntervalSeconds", json);
            StringAssert.DoesNotContain("layoutRefreshIntervalSeconds", json);
        }

        [Test]
        public void FromJson_SuppressLoopbackKeyMissing_KeepsDefaultTrue()
        {
            _instance.FromJson("{\"suppressLoopback\":false}");

            _instance.FromJson("{\"label\":\"studio\"}");

            Assert.IsTrue(_instance.SuppressLoopback);
        }

        [Test]
        public void CreateFromLegacy_CopiesAdvancedSenderValues()
        {
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            OscSenderRuntimeSettingsSO created = null;
            try
            {
                legacy.FromJson("{\"label\":\"old\",\"heartbeatIntervalSeconds\":3.0,\"suppressLoopback\":false}");

                created = OscSenderRuntimeSettingsSO.CreateFromLegacy(legacy);

                Assert.AreEqual("old", created.Label);
                Assert.IsFalse(created.SuppressLoopback);
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
