using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="OscSenderRuntimeSettingsSO"/>（OSC 送信の上級設定）の既定値・JSON 往復・不正値補正・
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
            Assert.AreEqual(
                OscSenderRuntimeSettingsSO.DefaultLayoutRefreshIntervalSeconds,
                _instance.LayoutRefreshIntervalSeconds);
            Assert.IsTrue(_instance.SuppressLoopback);
            Assert.IsTrue(_instance.IsDefault);
        }

        [Test]
        public void ToJson_ThenFromJson_RoundTripsAllFields()
        {
            _instance.FromJson("{\"label\":\"studio\",\"layoutRefreshIntervalSeconds\":2.5,\"suppressLoopback\":false}");

            string json = _instance.ToJson();
            var restored = ScriptableObject.CreateInstance<OscSenderRuntimeSettingsSO>();
            try
            {
                restored.FromJson(json);

                Assert.AreEqual("studio", restored.Label);
                Assert.AreEqual(2.5f, restored.LayoutRefreshIntervalSeconds);
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
        public void FromJson_NonPositiveLayoutRefreshInterval_NormalizesToDefault()
        {
            _instance.FromJson("{\"layoutRefreshIntervalSeconds\":0.0}");

            Assert.AreEqual(
                OscSenderRuntimeSettingsSO.DefaultLayoutRefreshIntervalSeconds,
                _instance.LayoutRefreshIntervalSeconds);
        }

        [Test]
        public void FromJson_LegacyHeartbeatIntervalKey_ReadsAsLayoutRefreshInterval()
        {
            _instance.FromJson("{\"heartbeatIntervalSeconds\":2.5}");

            Assert.AreEqual(2.5f, _instance.LayoutRefreshIntervalSeconds);
            StringAssert.DoesNotContain("heartbeatIntervalSeconds", _instance.ToJson());
        }

        [Test]
        public void FromJson_BothKeys_PrefersLayoutRefreshInterval()
        {
            _instance.FromJson("{\"heartbeatIntervalSeconds\":2.5,\"layoutRefreshIntervalSeconds\":7.0}");

            Assert.AreEqual(7f, _instance.LayoutRefreshIntervalSeconds);
        }

        [Test]
        public void FromJson_SuppressLoopbackKeyMissing_KeepsDefaultTrue()
        {
            _instance.FromJson("{\"suppressLoopback\":false}");

            _instance.FromJson("{\"layoutRefreshIntervalSeconds\":2.0}");

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
                Assert.AreEqual(3f, created.LayoutRefreshIntervalSeconds);
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
