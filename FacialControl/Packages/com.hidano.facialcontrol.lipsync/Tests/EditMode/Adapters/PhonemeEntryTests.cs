using System;
using Hidano.FacialControl.LipSync.Adapters.PhonemeEntries;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.LipSync.Tests.EditMode.Adapters
{
    [TestFixture]
    [MediumTest]
    public class PhonemeEntryTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_ULipSyncPhonemeEntryTests";
        private const string EntryFieldName = "_entry";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/PhonemeEntryTests_" + Guid.NewGuid().ToString("N") + ".asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_assetPath))
            {
                AssetDatabase.DeleteAsset(_assetPath);
                _assetPath = null;
            }

            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                var remaining = AssetDatabase.FindAssets(string.Empty, new[] { TempFolderPath });
                if (remaining == null || remaining.Length == 0)
                {
                    AssetDatabase.DeleteAsset(TempFolderPath);
                }
            }
        }

        [Test]
        public void BlendShapePhonemeEntry_SerializedPropertyRoundTrip_PreservesCommonAndBlendShapeFields()
        {
            var asset = ScriptableObject.CreateInstance<PhonemeEntryTestAsset>();
            AssetDatabase.CreateAsset(asset, _assetPath);

            using (var serialized = new SerializedObject(asset))
            {
                SetManagedReference(serialized, new BlendShapePhonemeEntry());

                var entry = serialized.FindProperty(EntryFieldName);
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "A";
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 87.5f;
                entry.FindPropertyRelative(nameof(BlendShapePhonemeEntry.BlendShapeName)).stringValue = "Mouth_A";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var loaded = SaveAndReload(asset);

            Assert.That(loaded.Entry, Is.InstanceOf<BlendShapePhonemeEntry>());
            var entryValue = (BlendShapePhonemeEntry)loaded.Entry;
            Assert.That(entryValue.PhonemeId, Is.EqualTo("A"));
            Assert.That(entryValue.MaxWeight, Is.EqualTo(87.5f).Within(1e-6f));
            Assert.That(entryValue.BlendShapeName, Is.EqualTo("Mouth_A"));

            using (var serialized = new SerializedObject(loaded))
            {
                var entry = serialized.FindProperty(EntryFieldName);
                Assert.That(entry.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(entry.managedReferenceFullTypename,
                    Does.Contain(typeof(BlendShapePhonemeEntry).FullName));
            }
        }

        [Test]
        public void AnimationClipPhonemeEntry_SerializedPropertyRoundTrip_PreservesCommonAndClipFields()
        {
            var asset = ScriptableObject.CreateInstance<PhonemeEntryTestAsset>();
            AssetDatabase.CreateAsset(asset, _assetPath);

            var clip = new AnimationClip { name = "Phoneme_O_TimeZero" };
            AssetDatabase.AddObjectToAsset(clip, asset);

            using (var serialized = new SerializedObject(asset))
            {
                SetManagedReference(serialized, new AnimationClipPhonemeEntry());

                var entry = serialized.FindProperty(EntryFieldName);
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "O";
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 62.25f;
                entry.FindPropertyRelative(nameof(AnimationClipPhonemeEntry.Clip)).objectReferenceValue = clip;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorUtility.SetDirty(clip);
            var loaded = SaveAndReload(asset);

            Assert.That(loaded.Entry, Is.InstanceOf<AnimationClipPhonemeEntry>());
            var entryValue = (AnimationClipPhonemeEntry)loaded.Entry;
            Assert.That(entryValue.PhonemeId, Is.EqualTo("O"));
            Assert.That(entryValue.MaxWeight, Is.EqualTo(62.25f).Within(1e-6f));
            Assert.That(entryValue.Clip, Is.Not.Null);
            Assert.That(entryValue.Clip.name, Is.EqualTo("Phoneme_O_TimeZero"));

            using (var serialized = new SerializedObject(loaded))
            {
                var entry = serialized.FindProperty(EntryFieldName);
                Assert.That(entry.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(entry.managedReferenceFullTypename,
                    Does.Contain(typeof(AnimationClipPhonemeEntry).FullName));
            }
        }

        [Test]
        public void ExpressionPhonemeEntry_SerializedPropertyRoundTrip_PreservesCommonAndExpressionId()
        {
            var asset = ScriptableObject.CreateInstance<PhonemeEntryTestAsset>();
            AssetDatabase.CreateAsset(asset, _assetPath);

            using (var serialized = new SerializedObject(asset))
            {
                SetManagedReference(serialized, new ExpressionPhonemeEntry());

                var entry = serialized.FindProperty(EntryFieldName);
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.PhonemeId)).stringValue = "I";
                entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight)).floatValue = 91.5f;
                entry.FindPropertyRelative("_expressionId").stringValue = "mouth_i";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var loaded = SaveAndReload(asset);

            Assert.That(loaded.Entry, Is.InstanceOf<ExpressionPhonemeEntry>());
            var entryValue = (ExpressionPhonemeEntry)loaded.Entry;
            Assert.That(entryValue.PhonemeId, Is.EqualTo("I"));
            Assert.That(entryValue.MaxWeight, Is.EqualTo(91.5f).Within(1e-6f));
            Assert.That(entryValue.ExpressionId, Is.EqualTo("mouth_i"));

            using (var serialized = new SerializedObject(loaded))
            {
                var entry = serialized.FindProperty(EntryFieldName);
                Assert.That(entry.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(entry.managedReferenceFullTypename,
                    Does.Contain(typeof(ExpressionPhonemeEntry).FullName));
            }
        }

        [Test]
        public void MaxWeight_SerializedProperty_PreservesZeroAndHundredPercentBoundaries()
        {
            var asset = ScriptableObject.CreateInstance<PhonemeEntryTestAsset>();
            SetEntryInMemory(asset, new BlendShapePhonemeEntry());

            using (var serialized = new SerializedObject(asset))
            {
                var entry = serialized.FindProperty(EntryFieldName);
                var maxWeight = entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight));

                maxWeight.floatValue = 0f;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(((BlendShapePhonemeEntry)asset.Entry).MaxWeight, Is.EqualTo(0f));

                serialized.Update();
                entry = serialized.FindProperty(EntryFieldName);
                maxWeight = entry.FindPropertyRelative(nameof(PhonemeEntryBase.MaxWeight));
                maxWeight.floatValue = 100f;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(((BlendShapePhonemeEntry)asset.Entry).MaxWeight, Is.EqualTo(100f),
                    "MaxWeight is stored as a 0..100 BlendShape percentage; snapshot builders normalize with maxWeight / 100.");
            }

            UnityEngine.Object.DestroyImmediate(asset);
        }

        private static void SetManagedReference(SerializedObject serialized, PhonemeEntryBase entryValue)
        {
            var entry = serialized.FindProperty(EntryFieldName);
            Assert.That(entry, Is.Not.Null);
            entry.managedReferenceValue = entryValue;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Update();
        }

        private static void SetEntryInMemory(PhonemeEntryTestAsset asset, PhonemeEntryBase entryValue)
        {
            using (var serialized = new SerializedObject(asset))
            {
                SetManagedReference(serialized, entryValue);
            }
        }

        private PhonemeEntryTestAsset SaveAndReload(PhonemeEntryTestAsset asset)
        {
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            Resources.UnloadAsset(asset);

            var loaded = AssetDatabase.LoadAssetAtPath<PhonemeEntryTestAsset>(_assetPath);
            Assert.That(loaded, Is.Not.Null);
            return loaded;
        }
    }
}
