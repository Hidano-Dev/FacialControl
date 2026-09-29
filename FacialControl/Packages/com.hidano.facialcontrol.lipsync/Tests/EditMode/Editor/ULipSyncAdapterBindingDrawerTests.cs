using System;
using System.Reflection;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.LipSync.Adapters.Devices;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using Hidano.FacialControl.LipSync.Editor.Inspector;
using Hidano.FacialControl.LipSync.Tests.Shared;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Editor
{
    /// <summary>
    /// <see cref="ULipSyncAdapterBindingDrawer"/> の smoke テスト。
    /// 「CreatePropertyGUI が例外なく生成できる」「PhonemeEntry 追加がアセット保存まで往復する」
    /// 「デバイス選択が <see cref="LipSyncDeviceStore"/> と往復する（デバイス未選択でリップシンクが
    /// 全滅する実機症状の入口）」だけを守る。
    /// </summary>
    [MediumTest]
    public class ULipSyncAdapterBindingDrawerTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_ULipSyncAdapterBindingDrawerTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private ULipSyncAdapterBindingDrawerTestAsset _asset;
        private SerializedObject _serializedObject;
        private SerializedProperty _bindingProperty;
        private string _assetPath;
        private FakePlayerPrefsBackend _backend;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakePlayerPrefsBackend();
            LipSyncDeviceStore.SetBackend(_backend);

            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/ULipSyncAdapterBindingDrawerTests_"
                + Guid.NewGuid().ToString("N") + ".asset";
            _asset = ScriptableObject.CreateInstance<ULipSyncAdapterBindingDrawerTestAsset>();
            _asset.Binding = new ULipSyncAdapterBinding();
            _serializedObject = new SerializedObject(_asset);
            _bindingProperty = _serializedObject.FindProperty(nameof(ULipSyncAdapterBindingDrawerTestAsset.Binding));
        }

        [TearDown]
        public void TearDown()
        {
            _serializedObject?.Dispose();

            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetDatabase.DeleteAsset(_assetPath);
                _assetPath = null;
            }

            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                string[] remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }

            if (_asset != null)
            {
                UnityEngine.Object.DestroyImmediate(_asset);
                _asset = null;
            }

            LipSyncDeviceStore.ResetBackend();
            _backend = null;
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyName);
            PlayerPrefs.DeleteKey(LipSyncDeviceStore.KeyDisambiguator);
        }

        // ====================================================================
        // smoke 1: 生成できる
        // ====================================================================

        [Test]
        public void CreatePropertyGUI_BindingProperty_ReturnsRootWithoutThrowing()
        {
            VisualElement root = null;
            Assert.DoesNotThrow(() => root = CreateDrawerRoot());

            Assert.That(root, Is.Not.Null);
            Assert.That(root.Q<PhonemeEntryListView>(), Is.Not.Null);
            Assert.That(root.Q<DeviceDescriptorPopup>(), Is.Not.Null);
        }

        // ====================================================================
        // smoke 2: 保存が通る（PhonemeEntry 追加 → アセット往復）
        // ====================================================================

        [Test]
        public void AddPhonemeEntry_SaveAndReload_RoundTripsSerializedValues()
        {
            VisualElement root = CreateDrawerRoot();
            var phonemeEntries = root.Q<PhonemeEntryListView>();

            phonemeEntries.AddEntry(PhonemeEntryListView.EntryKind.BlendShape);

            _serializedObject.Update();
            SerializedProperty entries = _bindingProperty.FindPropertyRelative("_phonemeEntries");
            Assert.That(entries.arraySize, Is.EqualTo(1));
            Assert.That(entries.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<BlendShapePhonemeEntry>());

            AssetDatabase.CreateAsset(_asset, _assetPath);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(_asset);
            _asset = null;

            var loaded = AssetDatabase.LoadAssetAtPath<ULipSyncAdapterBindingDrawerTestAsset>(_assetPath);
            Assert.That(loaded, Is.Not.Null);

            using (var serialized = new SerializedObject(loaded))
            {
                SerializedProperty loadedEntries = serialized
                    .FindProperty(nameof(ULipSyncAdapterBindingDrawerTestAsset.Binding))
                    .FindPropertyRelative("_phonemeEntries");

                Assert.That(loadedEntries.arraySize, Is.EqualTo(1));
                Assert.That(loadedEntries.GetArrayElementAtIndex(0).managedReferenceValue,
                    Is.InstanceOf<BlendShapePhonemeEntry>());
            }
        }

        // ====================================================================
        // Inspector で binding を新規追加した直後のプリセット（ApplyInitialDefaults）
        // ====================================================================

        [Test]
        public void ApplyInitialDefaults_EmptyEntries_SerializesFiveExpressionPhonemeEntriesInAiueoOrder()
        {
            ((ULipSyncAdapterBinding)_asset.Binding).ApplyInitialDefaults();

            _serializedObject.Update();
            SerializedProperty entries = _bindingProperty.FindPropertyRelative("_phonemeEntries");

            Assert.That(entries.arraySize, Is.EqualTo(5));
            string[] expectedIds = { "A", "I", "U", "E", "O" };
            for (int i = 0; i < expectedIds.Length; i++)
            {
                object entry = entries.GetArrayElementAtIndex(i).managedReferenceValue;
                Assert.That(entry, Is.InstanceOf<ExpressionPhonemeEntry>(), $"index {i}");
                Assert.That(((PhonemeEntryBase)entry).PhonemeId, Is.EqualTo(expectedIds[i]), $"index {i}");
            }
        }

        [Test]
        public void ApplyInitialDefaults_ExistingEntries_KeepsExistingEntriesUnchanged()
        {
            VisualElement root = CreateDrawerRoot();
            root.Q<PhonemeEntryListView>().AddEntry(PhonemeEntryListView.EntryKind.BlendShape);
            _serializedObject.Update();

            ((ULipSyncAdapterBinding)_asset.Binding).ApplyInitialDefaults();

            _serializedObject.Update();
            SerializedProperty entries = _bindingProperty.FindPropertyRelative("_phonemeEntries");
            Assert.That(entries.arraySize, Is.EqualTo(1));
            Assert.That(entries.GetArrayElementAtIndex(0).managedReferenceValue,
                Is.InstanceOf<BlendShapePhonemeEntry>());
        }

        // ====================================================================
        // デバイス選択と LipSyncDeviceStore の往復
        // ====================================================================

        [Test]
        public void DeviceDescriptorPopup_InitialState_LoadsFromDeviceStore()
        {
            _backend.SetString(LipSyncDeviceStore.KeyName, "Preset Mic");
            _backend.SetInt(LipSyncDeviceStore.KeyDisambiguator, 2);

            VisualElement root = CreateDrawerRoot();
            var popup = root.Q<DeviceDescriptorPopup>();

            Assert.That(popup, Is.Not.Null);
            Assert.That(popup.CurrentDescriptor.DeviceName, Is.EqualTo("Preset Mic"));
            Assert.That(popup.CurrentDescriptor.DisambiguatorIndex, Is.EqualTo(2));
        }

        [Test]
        public void DeviceDescriptorPopup_ManualOverrideChanged_PersistsToDeviceStore()
        {
            // panel 未接続では TextField の value 代入で ChangeEvent が発火しないため、
            // 変更ハンドラを直接呼び出して DeviceStore への到達を確認する。
            VisualElement root = CreateDrawerRoot();
            var popup = root.Q<DeviceDescriptorPopup>();

            InvokePrivate(popup, "ApplyDeviceNameFromManualOverride", "Disconnected Mic");

            Assert.That(_backend.GetString(LipSyncDeviceStore.KeyName, string.Empty),
                Is.EqualTo("Disconnected Mic"));
            Assert.That(_backend.SaveCallCount, Is.GreaterThanOrEqualTo(1));
        }

        private VisualElement CreateDrawerRoot()
        {
            _serializedObject.Update();
            _bindingProperty =
                _serializedObject.FindProperty(nameof(ULipSyncAdapterBindingDrawerTestAsset.Binding));
            var drawer = new ULipSyncAdapterBindingDrawer();
            return drawer.CreatePropertyGUI(_bindingProperty);
        }

        private static void InvokePrivate(
            DeviceDescriptorPopup popup,
            string methodName,
            params object[] args)
        {
            MethodInfo method = typeof(DeviceDescriptorPopup).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(popup, args);
        }
    }
}
