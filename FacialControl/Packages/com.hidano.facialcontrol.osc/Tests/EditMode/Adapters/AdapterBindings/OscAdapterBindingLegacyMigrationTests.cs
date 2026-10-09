using System;
using System.Linq;
using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Osc.Editor.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// 旧 <see cref="OscRuntimeSettingsSO"/> を割り当てた binding を、Inspector の「旧設定から移行」と同じ経路
    /// (<see cref="OscAdapterBindingSettingsSection"/>) で移行したときの結果を検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class OscAdapterBindingLegacyMigrationTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_OscAdapterBindingLegacyMigrationTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
            }
        }

        [Test]
        public void MigrateReceiver_LegacyInCollection_MovesPortAndCreatesAdvancedSubAsset()
        {
            AdapterRuntimeSettingsCollectionSO collection = CreateCollectionWithLegacy(
                "{\"listenPort\":9100,\"stalenessSeconds\":1.0,\"bundleMode\":\"individualMessage\"}",
                out OscRuntimeSettingsSO legacy);
            OscArKitRoundTripTestProfileSO profile = CreateProfile(
                new OscReceiverAdapterBinding { Slug = "osc", LegacySettings = legacy });

            bool migrated = OscAdapterBindingSettingsSection.MigrateReceiver(FindBindingProperty(profile, 0));

            Assert.That(migrated, Is.True);
            var receiver = (OscReceiverAdapterBinding)profile.AdapterBindings[0];
            Assert.That(receiver.LegacySettings, Is.Null, "移行後は旧設定への参照を外す。");
            Assert.That(receiver.Port, Is.EqualTo(9100));
            Assert.That(receiver.AdvancedSettings, Is.Not.Null, "既定値と異なる上級設定は新しい SO に移す。");
            Assert.That(receiver.AdvancedSettings.StalenessSeconds, Is.EqualTo(1f));
            Assert.That(receiver.AdvancedSettings.BundleMode, Is.EqualTo(BundleInterpretationMode.IndividualMessage));
            Assert.That(AssetDatabase.GetAssetPath(receiver.AdvancedSettings),
                Is.EqualTo(AssetDatabase.GetAssetPath(collection)), "旧設定と同じ Collection に保存する。");
            Assert.That(collection.Items.Contains(receiver.AdvancedSettings), Is.True,
                "Collection の一覧にも登録する。");
        }

        [Test]
        public void MigrateReceiver_LegacyWithDefaultAdvancedValues_LeavesAdvancedUnassigned()
        {
            CreateCollectionWithLegacy("{\"listenPort\":9100}", out OscRuntimeSettingsSO legacy);
            OscArKitRoundTripTestProfileSO profile = CreateProfile(
                new OscReceiverAdapterBinding { Slug = "osc", LegacySettings = legacy });

            OscAdapterBindingSettingsSection.MigrateReceiver(FindBindingProperty(profile, 0));

            var receiver = (OscReceiverAdapterBinding)profile.AdapterBindings[0];
            Assert.That(receiver.LegacySettings, Is.Null);
            Assert.That(receiver.Port, Is.EqualTo(9100));
            Assert.That(receiver.AdvancedSettings, Is.Null, "上級設定が既定値のままならアセットを作らない。");
        }

        [Test]
        public void MigrateReceiver_StandaloneLegacyAsset_SavesAdvancedAsSeparateAsset()
        {
            // 旧設定が単独アセットなら、旧アセットを消しても上級設定が残るよう別ファイルに保存する。
            var legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            legacy.FromJson("{\"listenPort\":9100,\"stalenessSeconds\":1.0}");
            string legacyPath = TempFolderPath + "/Legacy_" + Guid.NewGuid().ToString("N") + ".asset";
            AssetDatabase.CreateAsset(legacy, legacyPath);
            OscArKitRoundTripTestProfileSO profile = CreateProfile(
                new OscReceiverAdapterBinding { Slug = "osc", LegacySettings = legacy });

            OscAdapterBindingSettingsSection.MigrateReceiver(FindBindingProperty(profile, 0));

            var receiver = (OscReceiverAdapterBinding)profile.AdapterBindings[0];
            Assert.That(receiver.AdvancedSettings, Is.Not.Null);
            string advancedPath = AssetDatabase.GetAssetPath(receiver.AdvancedSettings);
            Assert.That(advancedPath, Is.Not.Empty);
            Assert.That(advancedPath, Is.Not.EqualTo(legacyPath));
        }

        [Test]
        public void MigrateSender_LegacyInCollection_MovesEndpointsAndCreatesAdvancedSubAsset()
        {
            CreateCollectionWithLegacy(
                "{\"heartbeatIntervalSeconds\":2.0,\"endpoints\":["
                + "{\"endpoint\":\"192.168.1.20\",\"port\":9000,\"enabled\":true,\"preset\":0},"
                + "{\"endpoint\":\"127.0.0.1\",\"port\":9001,\"enabled\":false,\"preset\":1}]}",
                out OscRuntimeSettingsSO legacy);
            OscArKitRoundTripTestProfileSO profile = CreateProfile(
                new OscSenderAdapterBinding { Slug = "osc-sender", LegacySettings = legacy });

            bool migrated = OscAdapterBindingSettingsSection.MigrateSender(FindBindingProperty(profile, 0));

            Assert.That(migrated, Is.True);
            var sender = (OscSenderAdapterBinding)profile.AdapterBindings[0];
            Assert.That(sender.LegacySettings, Is.Null);
            Assert.That(sender.Endpoints.Count, Is.EqualTo(2));
            Assert.That(sender.Endpoints[0].endpoint, Is.EqualTo("192.168.1.20"));
            Assert.That(sender.Endpoints[0].port, Is.EqualTo(9000));
            Assert.That(sender.Endpoints[0].enabled, Is.True);
            Assert.That(sender.Endpoints[1].enabled, Is.False);
            Assert.That(sender.AdvancedSettings, Is.Not.Null);
            Assert.That(sender.AdvancedSettings.LayoutRefreshIntervalSeconds, Is.EqualTo(2f));
        }

        [Test]
        public void MigrateSender_LegacySenderDisabled_MigratesEndpointsAsDisabled()
        {
            // 旧設定で送信を止めていた場合、移行後も送信しない状態を保つ。
            CreateCollectionWithLegacy(
                "{\"senderEnabled\":false,\"endpoints\":[{\"endpoint\":\"127.0.0.1\",\"port\":9000,\"enabled\":true,\"preset\":0}]}",
                out OscRuntimeSettingsSO legacy);
            OscArKitRoundTripTestProfileSO profile = CreateProfile(
                new OscSenderAdapterBinding { Slug = "osc-sender", LegacySettings = legacy });

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("senderEnabled=false"));
            OscAdapterBindingSettingsSection.MigrateSender(FindBindingProperty(profile, 0));

            var sender = (OscSenderAdapterBinding)profile.AdapterBindings[0];
            Assert.That(sender.Endpoints.Count, Is.EqualTo(1));
            Assert.That(sender.Endpoints[0].enabled, Is.False);
        }

        private static AdapterRuntimeSettingsCollectionSO CreateCollectionWithLegacy(
            string legacyJson,
            out OscRuntimeSettingsSO legacy)
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            string path = TempFolderPath + "/Collection_" + Guid.NewGuid().ToString("N") + ".asset";
            AssetDatabase.CreateAsset(collection, path);

            legacy = ScriptableObject.CreateInstance<OscRuntimeSettingsSO>();
            legacy.name = "OscRuntimeSettings";
            legacy.FromJson(legacyJson);
            AssetDatabase.AddObjectToAsset(legacy, collection);

            var collectionObject = new SerializedObject(collection);
            SerializedProperty items = collectionObject.FindProperty("_items");
            items.InsertArrayElementAtIndex(0);
            items.GetArrayElementAtIndex(0).objectReferenceValue = legacy;
            collectionObject.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
            return collection;
        }

        private static OscArKitRoundTripTestProfileSO CreateProfile(Hidano.FacialControl.Domain.Adapters.AdapterBindingBase binding)
        {
            var profile = ScriptableObject.CreateInstance<OscArKitRoundTripTestProfileSO>();
            profile.WritableAdapterBindings.Add(binding);
            string path = TempFolderPath + "/Profile_" + Guid.NewGuid().ToString("N") + ".asset";
            AssetDatabase.CreateAsset(profile, path);
            AssetDatabase.SaveAssets();
            return profile;
        }

        private static SerializedProperty FindBindingProperty(OscArKitRoundTripTestProfileSO profile, int index)
        {
            var serializedObject = new SerializedObject(profile);
            SerializedProperty list = serializedObject.FindProperty("_adapterBindings");
            return list.GetArrayElementAtIndex(index);
        }
    }
}
