using System.Collections.Generic;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Domain.Models;
using NUnit.Framework;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="OscRuntimeSettingsSO"/> の単一インスタンスに対する EditMode テスト。
    /// Receiver / Sender 両セクションの既定値と getter、<c>_receiverEnabled</c> / <c>_senderEnabled</c> トグル、
    /// <c>JsonUtility</c> 直叩きでの SerializeField 名ベースの往復、
    /// <see cref="ISerializationCallbackReceiver.OnAfterDeserialize"/> による不正値補正・enum 正規化を検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OscRuntimeSettingsSOTests : SizedTestFixture
    {
        private OscRuntimeSettingsSO _instance;

        [SetUp]
        public void SetUp()
        {
            _instance = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
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

        // ---------------------------------------------------------------
        // 既定値 / getter
        // ---------------------------------------------------------------

        [Test]
        public void Defaults_OnFreshInstance_ReturnsExpectedReceiverDefaults()
        {
            Assert.AreEqual(OscRuntimeSettingsSO.DefaultListenEndpoint, _instance.ListenEndpoint);
            Assert.AreEqual(9001, _instance.ListenPort);
            Assert.AreEqual(0f, _instance.StalenessSeconds);
            Assert.AreEqual(FailSafeMode.RevertToBase, _instance.FailSafeMode);
            Assert.IsTrue(_instance.ConsistencyCheckWarnLog);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _instance.BundleMode);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void Defaults_OnFreshInstance_ReturnsExpectedSenderDefaults()
        {
            Assert.IsNotNull(_instance.Endpoints);
            Assert.AreEqual(0, _instance.Endpoints.Count);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _instance.HeartbeatIntervalSeconds);
            Assert.IsTrue(_instance.SuppressLoopback);
        }

        [Test]
        public void ReceiverEnabled_OnFreshInstance_ReturnsTrue()
        {
            Assert.IsTrue(_instance.ReceiverEnabled);
        }

        [Test]
        public void SenderEnabled_OnFreshInstance_ReturnsTrue()
        {
            Assert.IsTrue(_instance.SenderEnabled);
        }

        [Test]
        public void Getters_AfterSerializedFieldAssignment_ReturnAssignedValues()
        {
            AssignReceiverFields(
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f);
            AssignSenderFields(
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            Assert.AreEqual("192.168.1.10", _instance.ListenEndpoint);
            Assert.AreEqual(9100, _instance.ListenPort);
            Assert.AreEqual(1.5f, _instance.StalenessSeconds);
            Assert.AreEqual(FailSafeMode.HoldLastValue, _instance.FailSafeMode);
            Assert.IsFalse(_instance.ConsistencyCheckWarnLog);
            Assert.AreEqual(BundleInterpretationMode.IndividualMessage, _instance.BundleMode);
            Assert.AreEqual(12.5f, _instance.BundleAccumulationTimeoutMs);

            Assert.AreEqual(1, _instance.Endpoints.Count);
            Assert.AreEqual("10.0.0.1", _instance.Endpoints[0].endpoint);
            Assert.AreEqual(9200, _instance.Endpoints[0].port);
            Assert.AreEqual(7.5f, _instance.HeartbeatIntervalSeconds);
            Assert.IsFalse(_instance.SuppressLoopback);
        }

        // ---------------------------------------------------------------
        // JsonUtility 直叩き (SerializeField 名ベース) の往復
        // ---------------------------------------------------------------

        [Test]
        public void JsonUtility_ToJsonAndFromJsonOverwrite_PreservesReceiverAndSenderFields()
        {
            AssignReceiverFields(
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f);
            AssignSenderFields(
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200, true, AddressPresetKind.VRChat),
                    new OscSenderEndpointConfig("10.0.0.2", 9300, false, AddressPresetKind.ARKit),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            var json = JsonUtility.ToJson(_instance);
            var restored = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            try
            {
                JsonUtility.FromJsonOverwrite(json, restored);

                Assert.AreEqual(_instance.ListenEndpoint, restored.ListenEndpoint);
                Assert.AreEqual(_instance.ListenPort, restored.ListenPort);
                Assert.AreEqual(_instance.StalenessSeconds, restored.StalenessSeconds);
                Assert.AreEqual(_instance.FailSafeMode, restored.FailSafeMode);
                Assert.AreEqual(_instance.ConsistencyCheckWarnLog, restored.ConsistencyCheckWarnLog);
                Assert.AreEqual(_instance.BundleMode, restored.BundleMode);
                Assert.AreEqual(_instance.BundleAccumulationTimeoutMs, restored.BundleAccumulationTimeoutMs);

                Assert.AreEqual(_instance.Endpoints.Count, restored.Endpoints.Count);
                for (int i = 0; i < _instance.Endpoints.Count; i++)
                {
                    Assert.AreEqual(_instance.Endpoints[i].endpoint, restored.Endpoints[i].endpoint);
                    Assert.AreEqual(_instance.Endpoints[i].port, restored.Endpoints[i].port);
                    Assert.AreEqual(_instance.Endpoints[i].enabled, restored.Endpoints[i].enabled);
                    Assert.AreEqual(_instance.Endpoints[i].preset, restored.Endpoints[i].preset);
                }
                Assert.AreEqual(_instance.HeartbeatIntervalSeconds, restored.HeartbeatIntervalSeconds);
                Assert.AreEqual(_instance.SuppressLoopback, restored.SuppressLoopback);
            }
            finally
            {
                Object.DestroyImmediate(restored);
            }
        }

        [Test]
        public void JsonUtility_ToJson_ContainsReceiverAndSenderFieldValues()
        {
            AssignReceiverFields(
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f);
            AssignSenderFields(
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            var json = JsonUtility.ToJson(_instance);

            // JsonUtility は SerializeField 名 (_camelCase) をそのまま JSON キーに用いる。
            // `listenPort` 等の正規化キーへの変換と enum の文字列化は OscRuntimeSettingsSO.ToJson 側の責務
            // (OscRuntimeSettingsSOJsonRoundTripTests で検証)。ここでは enum は int で出力される。
            // Receiver section
            StringAssert.Contains("\"_listenEndpoint\":\"192.168.1.10\"", json);
            StringAssert.Contains("\"_listenPort\":9100", json);
            StringAssert.Contains("\"_stalenessSeconds\":1.5", json);
            StringAssert.Contains("\"_failSafeMode\":1", json);
            StringAssert.Contains("\"_consistencyCheckWarnLog\":false", json);
            StringAssert.Contains("\"_bundleMode\":1", json);
            StringAssert.Contains("\"_bundleAccumulationTimeoutMs\":12.5", json);

            // Sender section
            StringAssert.Contains("\"_endpoints\":[", json);
            StringAssert.Contains("\"endpoint\":\"10.0.0.1\"", json);
            StringAssert.Contains("\"port\":9200", json);
            StringAssert.Contains("\"_heartbeatIntervalSeconds\":7.5", json);
            StringAssert.Contains("\"_suppressLoopback\":false", json);
        }

        // ---------------------------------------------------------------
        // OnAfterDeserialize: 不正値補正 / enum 正規化
        // ---------------------------------------------------------------

        [Test]
        public void OnAfterDeserialize_ListenPortZero_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenPort\":0}", _instance);

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _instance.ListenPort);
        }

        [Test]
        public void OnAfterDeserialize_ListenPortOutOfRange_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenPort\":70000}", _instance);

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _instance.ListenPort);
        }

        [Test]
        public void OnAfterDeserialize_BundleAccumulationTimeoutNegative_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_bundleAccumulationTimeoutMs\":-1}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void OnAfterDeserialize_BundleAccumulationTimeoutZero_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_bundleAccumulationTimeoutMs\":0}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _instance.BundleAccumulationTimeoutMs);
        }

        [Test]
        public void OnAfterDeserialize_HeartbeatIntervalNegative_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_heartbeatIntervalSeconds\":-1}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _instance.HeartbeatIntervalSeconds);
        }

        [Test]
        public void OnAfterDeserialize_HeartbeatIntervalZero_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_heartbeatIntervalSeconds\":0}", _instance);

            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _instance.HeartbeatIntervalSeconds);
        }

        [Test]
        public void OnAfterDeserialize_StalenessNegative_NormalizesToZero()
        {
            JsonUtility.FromJsonOverwrite("{\"_stalenessSeconds\":-1}", _instance);

            Assert.AreEqual(0f, _instance.StalenessSeconds);
        }

        [Test]
        public void OnAfterDeserialize_ListenEndpointWhitespace_NormalizesToDefault()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenEndpoint\":\"   \"}", _instance);

            Assert.AreEqual(OscRuntimeSettingsSO.DefaultListenEndpoint, _instance.ListenEndpoint);
        }

        [Test]
        public void OnAfterDeserialize_ListenEndpointWithWhitespacePadding_TrimsWhitespace()
        {
            JsonUtility.FromJsonOverwrite("{\"_listenEndpoint\":\"  192.168.1.10  \"}", _instance);

            Assert.AreEqual("192.168.1.10", _instance.ListenEndpoint);
        }

        [Test]
        public void OnAfterDeserialize_FailSafeModeUndefined_NormalizesToRevertToBase()
        {
            JsonUtility.FromJsonOverwrite("{\"_failSafeMode\":999}", _instance);

            Assert.AreEqual(FailSafeMode.RevertToBase, _instance.FailSafeMode);
        }

        [Test]
        public void OnAfterDeserialize_BundleModeUndefined_NormalizesToAtomicSwap()
        {
            JsonUtility.FromJsonOverwrite("{\"_bundleMode\":999}", _instance);

            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _instance.BundleMode);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private void AssignReceiverFields(
            string listenEndpoint,
            int listenPort,
            float stalenessSeconds,
            FailSafeMode failSafeMode,
            bool consistencyCheckWarnLog,
            BundleInterpretationMode bundleMode,
            float bundleAccumulationTimeoutMs)
        {
            var so = new UnityEditor.SerializedObject(_instance);
            so.FindProperty("_listenEndpoint").stringValue = listenEndpoint;
            so.FindProperty("_listenPort").intValue = listenPort;
            so.FindProperty("_stalenessSeconds").floatValue = stalenessSeconds;
            so.FindProperty("_failSafeMode").enumValueIndex = (int)failSafeMode;
            so.FindProperty("_consistencyCheckWarnLog").boolValue = consistencyCheckWarnLog;
            so.FindProperty("_bundleMode").enumValueIndex = (int)bundleMode;
            so.FindProperty("_bundleAccumulationTimeoutMs").floatValue = bundleAccumulationTimeoutMs;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private void AssignSenderFields(
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            float heartbeatIntervalSeconds,
            bool suppressLoopback)
        {
            var so = new UnityEditor.SerializedObject(_instance);
            var endpointsProperty = so.FindProperty("_endpoints");
            endpointsProperty.arraySize = endpoints.Count;
            for (var i = 0; i < endpoints.Count; i++)
            {
                var element = endpointsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("endpoint").stringValue = endpoints[i].endpoint;
                element.FindPropertyRelative("port").intValue = endpoints[i].port;
                element.FindPropertyRelative("enabled").boolValue = endpoints[i].enabled;
                element.FindPropertyRelative("preset").enumValueIndex = (int)endpoints[i].preset;
            }

            so.FindProperty("_heartbeatIntervalSeconds").floatValue = heartbeatIntervalSeconds;
            so.FindProperty("_suppressLoopback").boolValue = suppressLoopback;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// <see cref="OscRuntimeSettingsSO"/> 自身の <c>ToJson</c> / <c>FromJson</c> による往復テスト。
    /// 全フィールドを設定した source SO を ToJson → 別 SO に FromJson で復元したとき全フィールドが一致すること、
    /// enum (FailSafeMode / BundleInterpretationMode) が JSON 上で文字列として書き出されること、
    /// 不正な enum 文字列・数値・空 JSON が既定値へ正規化されることを検証する。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class OscRuntimeSettingsSOJsonRoundTripTests : SizedTestFixture
    {
        private OscRuntimeSettingsSO _source;
        private OscRuntimeSettingsSO _restored;

        [SetUp]
        public void SetUp()
        {
            _source = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            _restored = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_source != null)
            {
                Object.DestroyImmediate(_source);
                _source = null;
            }

            if (_restored != null)
            {
                Object.DestroyImmediate(_restored);
                _restored = null;
            }
        }

        [Test]
        public void ToJson_FromJson_PreservesAllFields()
        {
            AssignAllFields(
                _source,
                label: "prod-osc",
                schemaVersion: 1,
                receiverEnabled: false,
                listenEndpoint: "192.168.1.10",
                listenPort: 9100,
                stalenessSeconds: 1.5f,
                failSafeMode: FailSafeMode.HoldLastValue,
                consistencyCheckWarnLog: false,
                bundleMode: BundleInterpretationMode.IndividualMessage,
                bundleAccumulationTimeoutMs: 12.5f,
                senderEnabled: false,
                endpoints: new List<OscSenderEndpointConfig>
                {
                    new OscSenderEndpointConfig("10.0.0.1", 9200, true, AddressPresetKind.VRChat),
                    new OscSenderEndpointConfig("10.0.0.2", 9300, false, AddressPresetKind.ARKit),
                },
                heartbeatIntervalSeconds: 7.5f,
                suppressLoopback: false);

            string json = _source.ToJson();
            _restored.FromJson(json);

            Assert.AreEqual(_source.Label, _restored.Label);
            Assert.AreEqual(_source.SchemaVersion, _restored.SchemaVersion);
            Assert.AreEqual(_source.ReceiverEnabled, _restored.ReceiverEnabled);
            Assert.AreEqual(_source.ListenEndpoint, _restored.ListenEndpoint);
            Assert.AreEqual(_source.ListenPort, _restored.ListenPort);
            Assert.AreEqual(_source.StalenessSeconds, _restored.StalenessSeconds);
            Assert.AreEqual(_source.FailSafeMode, _restored.FailSafeMode);
            Assert.AreEqual(_source.ConsistencyCheckWarnLog, _restored.ConsistencyCheckWarnLog);
            Assert.AreEqual(_source.BundleMode, _restored.BundleMode);
            Assert.AreEqual(_source.BundleAccumulationTimeoutMs, _restored.BundleAccumulationTimeoutMs);
            Assert.AreEqual(_source.SenderEnabled, _restored.SenderEnabled);
            Assert.AreEqual(_source.HeartbeatIntervalSeconds, _restored.HeartbeatIntervalSeconds);
            Assert.AreEqual(_source.SuppressLoopback, _restored.SuppressLoopback);

            Assert.AreEqual(_source.Endpoints.Count, _restored.Endpoints.Count);
            for (int i = 0; i < _source.Endpoints.Count; i++)
            {
                Assert.AreEqual(_source.Endpoints[i].endpoint, _restored.Endpoints[i].endpoint);
                Assert.AreEqual(_source.Endpoints[i].port, _restored.Endpoints[i].port);
                Assert.AreEqual(_source.Endpoints[i].enabled, _restored.Endpoints[i].enabled);
                Assert.AreEqual(_source.Endpoints[i].preset, _restored.Endpoints[i].preset);
            }
        }

        [Test]
        public void ToJson_FailSafeMode_WrittenAsString()
        {
            AssignFailSafeMode(_source, FailSafeMode.HoldLastValue);

            string json = _source.ToJson();

            StringAssert.Contains("\"failSafeMode\": \"holdLastValue\"", json);
        }

        [Test]
        public void ToJson_BundleMode_WrittenAsString()
        {
            AssignBundleMode(_source, BundleInterpretationMode.IndividualMessage);

            string json = _source.ToJson();

            StringAssert.Contains("\"bundleMode\": \"individualMessage\"", json);
        }

        [Test]
        public void FromJson_InvalidEnumStrings_NormalizedToDefaults()
        {
            string json = "{\"failSafeMode\":\"unknown\",\"bundleMode\":\"unknown\"}";

            _restored.FromJson(json);

            Assert.AreEqual(FailSafeMode.RevertToBase, _restored.FailSafeMode);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _restored.BundleMode);
        }

        [Test]
        public void FromJson_InvalidNumericFields_NormalizedToDefaults()
        {
            string json =
                "{\"listenPort\":0,\"bundleAccumulationTimeoutMs\":-1," +
                "\"heartbeatIntervalSeconds\":-1,\"stalenessSeconds\":-1}";

            _restored.FromJson(json);

            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _restored.ListenPort);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultBundleAccumulationTimeoutMs,
                _restored.BundleAccumulationTimeoutMs);
            Assert.AreEqual(
                OscRuntimeSettingsSO.DefaultHeartbeatIntervalSeconds,
                _restored.HeartbeatIntervalSeconds);
            Assert.AreEqual(0f, _restored.StalenessSeconds);
        }

        [Test]
        public void FromJson_EmptyJson_AppliesDefaults()
        {
            _restored.FromJson(string.Empty);

            Assert.AreEqual(OscRuntimeSettingsSO.DefaultListenEndpoint, _restored.ListenEndpoint);
            Assert.AreEqual(OscConfiguration.DefaultReceivePort, _restored.ListenPort);
            Assert.AreEqual(FailSafeMode.RevertToBase, _restored.FailSafeMode);
            Assert.AreEqual(BundleInterpretationMode.AtomicSwap, _restored.BundleMode);
        }

        // ---------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------

        private static void AssignAllFields(
            OscRuntimeSettingsSO target,
            string label,
            int schemaVersion,
            bool receiverEnabled,
            string listenEndpoint,
            int listenPort,
            float stalenessSeconds,
            FailSafeMode failSafeMode,
            bool consistencyCheckWarnLog,
            BundleInterpretationMode bundleMode,
            float bundleAccumulationTimeoutMs,
            bool senderEnabled,
            IReadOnlyList<OscSenderEndpointConfig> endpoints,
            float heartbeatIntervalSeconds,
            bool suppressLoopback)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_label").stringValue = label;
            so.FindProperty("_schemaVersion").intValue = schemaVersion;
            so.FindProperty("_receiverEnabled").boolValue = receiverEnabled;
            so.FindProperty("_listenEndpoint").stringValue = listenEndpoint;
            so.FindProperty("_listenPort").intValue = listenPort;
            so.FindProperty("_stalenessSeconds").floatValue = stalenessSeconds;
            so.FindProperty("_failSafeMode").enumValueIndex = (int)failSafeMode;
            so.FindProperty("_consistencyCheckWarnLog").boolValue = consistencyCheckWarnLog;
            so.FindProperty("_bundleMode").enumValueIndex = (int)bundleMode;
            so.FindProperty("_bundleAccumulationTimeoutMs").floatValue = bundleAccumulationTimeoutMs;
            so.FindProperty("_senderEnabled").boolValue = senderEnabled;
            so.FindProperty("_heartbeatIntervalSeconds").floatValue = heartbeatIntervalSeconds;
            so.FindProperty("_suppressLoopback").boolValue = suppressLoopback;

            var endpointsProperty = so.FindProperty("_endpoints");
            endpointsProperty.arraySize = endpoints.Count;
            for (var i = 0; i < endpoints.Count; i++)
            {
                var element = endpointsProperty.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("endpoint").stringValue = endpoints[i].endpoint;
                element.FindPropertyRelative("port").intValue = endpoints[i].port;
                element.FindPropertyRelative("enabled").boolValue = endpoints[i].enabled;
                element.FindPropertyRelative("preset").enumValueIndex = (int)endpoints[i].preset;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignFailSafeMode(OscRuntimeSettingsSO target, FailSafeMode mode)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_failSafeMode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AssignBundleMode(OscRuntimeSettingsSO target, BundleInterpretationMode mode)
        {
            var so = new UnityEditor.SerializedObject(target);
            so.FindProperty("_bundleMode").enumValueIndex = (int)mode;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
