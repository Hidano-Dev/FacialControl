using Hidano.FacialControl.Adapters.RuntimeSettings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Adapters.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsCollectionSO"/> の契約テスト。
    /// in-memory インスタンスに対する <c>TryFind&lt;T&gt;()</c>（型 / ラベル検索）、<c>IndexOf</c> と、
    /// AssetDatabase 上の実アセットで sub-asset を追加 / 削除した際に
    /// 残存 sub-asset の <c>_label</c> / <c>_schemaVersion</c> が保持されることを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AdapterRuntimeSettingsCollectionSOTests : SizedTestFixture
    {
        private AdapterRuntimeSettingsCollectionSO _collection;

        [SetUp]
        public void SetUp()
        {
            _collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_collection != null)
            {
                Object.DestroyImmediate(_collection);
                _collection = null;
            }
        }

        [Test]
        public void Items_OnFreshInstance_ReturnsEmptyList()
        {
            Assert.IsNotNull(_collection.Items);
            Assert.AreEqual(0, _collection.Items.Count);
        }

        [Test]
        public void TryFind_OnEmptyCollection_ReturnsNull()
        {
            var result = _collection.TryFind<FakeAlphaSettings>();

            Assert.IsNull(result);
        }

        [Test]
        public void TryFindWithLabel_OnEmptyCollection_ReturnsNull()
        {
            var result = _collection.TryFind<FakeAlphaSettings>("primary");

            Assert.IsNull(result);
        }

        [Test]
        public void TryFind_WithMatchingType_ReturnsFirstInstance()
        {
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();
            var beta = ScriptableObject.CreateInstance<FakeBetaSettings>();

            try
            {
                var so = new SerializedObject(_collection);
                var items = so.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = alpha;
                items.GetArrayElementAtIndex(1).objectReferenceValue = beta;
                so.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>();

                Assert.AreSame(alpha, result);
            }
            finally
            {
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(beta);
            }
        }

        [Test]
        public void TryFind_WithoutMatchingType_ReturnsNull()
        {
            var beta = ScriptableObject.CreateInstance<FakeBetaSettings>();

            try
            {
                var so = new SerializedObject(_collection);
                var items = so.FindProperty("_items");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = beta;
                so.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>();

                Assert.IsNull(result);
            }
            finally
            {
                Object.DestroyImmediate(beta);
            }
        }

        [Test]
        public void TryFindWithLabel_MatchingTypeAndLabel_ReturnsInstance()
        {
            var first = ScriptableObject.CreateInstance<FakeAlphaSettings>();
            var second = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                AssignLabel(first, "primary");
                AssignLabel(second, "secondary");

                var collectionSo = new SerializedObject(_collection);
                var items = collectionSo.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = first;
                items.GetArrayElementAtIndex(1).objectReferenceValue = second;
                collectionSo.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>("secondary");

                Assert.AreSame(second, result);
            }
            finally
            {
                Object.DestroyImmediate(first);
                Object.DestroyImmediate(second);
            }
        }

        [Test]
        public void TryFindWithLabel_NoLabelMatch_ReturnsNull()
        {
            var first = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                AssignLabel(first, "primary");

                var collectionSo = new SerializedObject(_collection);
                var items = collectionSo.FindProperty("_items");
                items.arraySize = 1;
                items.GetArrayElementAtIndex(0).objectReferenceValue = first;
                collectionSo.ApplyModifiedPropertiesWithoutUndo();

                var result = _collection.TryFind<FakeAlphaSettings>("missing");

                Assert.IsNull(result);
            }
            finally
            {
                Object.DestroyImmediate(first);
            }
        }

        [Test]
        public void IndexOf_ItemNotPresent_ReturnsMinusOne()
        {
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();

            try
            {
                Assert.AreEqual(-1, _collection.IndexOf(alpha));
            }
            finally
            {
                Object.DestroyImmediate(alpha);
            }
        }

        [Test]
        public void IndexOf_Null_ReturnsMinusOne()
        {
            Assert.AreEqual(-1, _collection.IndexOf(null));
        }

        [Test]
        public void IndexOf_ItemPresent_ReturnsIndex()
        {
            var alpha = ScriptableObject.CreateInstance<FakeAlphaSettings>();
            var beta = ScriptableObject.CreateInstance<FakeBetaSettings>();

            try
            {
                var so = new SerializedObject(_collection);
                var items = so.FindProperty("_items");
                items.arraySize = 2;
                items.GetArrayElementAtIndex(0).objectReferenceValue = alpha;
                items.GetArrayElementAtIndex(1).objectReferenceValue = beta;
                so.ApplyModifiedPropertiesWithoutUndo();

                Assert.AreEqual(0, _collection.IndexOf(alpha));
                Assert.AreEqual(1, _collection.IndexOf(beta));
            }
            finally
            {
                Object.DestroyImmediate(alpha);
                Object.DestroyImmediate(beta);
            }
        }

        private static void AssignLabel(AdapterRuntimeSettingsBase target, string label)
        {
            var so = new SerializedObject(target);
            so.FindProperty("_label").stringValue = label;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    /// <summary>
    /// AssetDatabase 上に実アセットとして保存した <see cref="AdapterRuntimeSettingsCollectionSO"/> に
    /// sub-asset を追加 / 削除し、再インポート後も残存 sub-asset の <c>_label</c> / <c>_schemaVersion</c>
    /// が消失しないことを検証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AdapterRuntimeSettingsCollectionSOPersistedSubAssetTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "Temp_AdapterRuntimeSettingsCollectionSOPersistedSubAssetTests";
        private const string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _collectionAssetPath;

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
            }

            AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            _collectionAssetPath = TempFolderPath + "/TestCollection.asset";
        }

        [TearDown]
        public void TearDown()
        {
            if (AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.DeleteAsset(TempFolderPath);
            }

            AssetDatabase.Refresh();
        }

        [Test]
        public void SubAssets_AfterAddingMultipleEntries_PreserveLabelAndSchemaVersion()
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            AssetDatabase.CreateAsset(collection, _collectionAssetPath);

            var alpha = AddSubAsset<FakeAlphaSettings>(collection, "alpha", schemaVersion: 1);
            var beta = AddSubAsset<FakeBetaSettings>(collection, "beta", schemaVersion: 2);
            var gamma = AddSubAsset<FakeGammaSettings>(collection, "gamma", schemaVersion: 3);
            AppendToItems(collection, alpha, beta, gamma);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath, ImportAssetOptions.ForceUpdate);

            var loaded = AssetDatabase.LoadAssetAtPath<AdapterRuntimeSettingsCollectionSO>(_collectionAssetPath);
            Assert.IsNotNull(loaded, "Collection アセットを再ロードできませんでした。");
            Assert.AreEqual(3, loaded.Items.Count, "sub-asset 3 件分が _items に反映されている必要があります。");

            var loadedAlpha = loaded.TryFind<FakeAlphaSettings>("alpha");
            Assert.IsNotNull(loadedAlpha, "alpha sub-asset が再ロード後も TryFind で取得できる必要があります。");
            Assert.AreEqual("alpha", loadedAlpha.Label);
            Assert.AreEqual(1, loadedAlpha.SchemaVersion);

            var loadedBeta = loaded.TryFind<FakeBetaSettings>("beta");
            Assert.IsNotNull(loadedBeta, "beta sub-asset が再ロード後も TryFind で取得できる必要があります。");
            Assert.AreEqual("beta", loadedBeta.Label);
            Assert.AreEqual(2, loadedBeta.SchemaVersion);

            var loadedGamma = loaded.TryFind<FakeGammaSettings>("gamma");
            Assert.IsNotNull(loadedGamma, "gamma sub-asset が再ロード後も TryFind で取得できる必要があります。");
            Assert.AreEqual("gamma", loadedGamma.Label);
            Assert.AreEqual(3, loadedGamma.SchemaVersion);
        }

        [Test]
        public void RemoveSubAsset_MiddleEntry_PreservesRemainingLabelAndSchemaVersion()
        {
            var collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            AssetDatabase.CreateAsset(collection, _collectionAssetPath);

            var alpha = AddSubAsset<FakeAlphaSettings>(collection, "alpha", schemaVersion: 11);
            var beta = AddSubAsset<FakeBetaSettings>(collection, "beta", schemaVersion: 22);
            var gamma = AddSubAsset<FakeGammaSettings>(collection, "gamma", schemaVersion: 33);
            AppendToItems(collection, alpha, beta, gamma);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath, ImportAssetOptions.ForceUpdate);

            var preRemove = AssetDatabase.LoadAssetAtPath<AdapterRuntimeSettingsCollectionSO>(_collectionAssetPath);
            Assert.AreEqual(3, preRemove.Items.Count, "削除前は 3 件揃っている必要があります。");

            var middle = preRemove.TryFind<FakeBetaSettings>("beta");
            Assert.IsNotNull(middle, "削除対象の beta sub-asset を取得できる必要があります。");

            RemoveSubAssetFromCollection(preRemove, middle);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath, ImportAssetOptions.ForceUpdate);

            var loaded = AssetDatabase.LoadAssetAtPath<AdapterRuntimeSettingsCollectionSO>(_collectionAssetPath);
            Assert.IsNotNull(loaded, "削除後の Collection を再ロードできませんでした。");
            Assert.AreEqual(2, loaded.Items.Count, "中央 sub-asset 削除後は 2 件残る必要があります。");

            var loadedAlpha = loaded.TryFind<FakeAlphaSettings>("alpha");
            Assert.IsNotNull(loadedAlpha, "alpha sub-asset が削除後も TryFind で取得できる必要があります。");
            Assert.AreEqual("alpha", loadedAlpha.Label, "alpha の _label は削除前と同値である必要があります。");
            Assert.AreEqual(11, loadedAlpha.SchemaVersion, "alpha の _schemaVersion は削除前と同値である必要があります。");

            var loadedGamma = loaded.TryFind<FakeGammaSettings>("gamma");
            Assert.IsNotNull(loadedGamma, "gamma sub-asset が削除後も TryFind で取得できる必要があります。");
            Assert.AreEqual("gamma", loadedGamma.Label, "gamma の _label は削除前と同値である必要があります。");
            Assert.AreEqual(33, loadedGamma.SchemaVersion, "gamma の _schemaVersion は削除前と同値である必要があります。");

            Assert.IsNull(loaded.TryFind<FakeBetaSettings>("beta"), "削除対象 beta は再ロード後に存在しない必要があります。");
        }

        private static T AddSubAsset<T>(
            AdapterRuntimeSettingsCollectionSO collection,
            string label,
            int schemaVersion)
            where T : AdapterRuntimeSettingsBase
        {
            var sub = ScriptableObject.CreateInstance<T>();
            sub.name = label;
            AssetDatabase.AddObjectToAsset(sub, collection);

            var so = new SerializedObject(sub);
            so.FindProperty("_label").stringValue = label;
            so.FindProperty("_schemaVersion").intValue = schemaVersion;
            so.ApplyModifiedPropertiesWithoutUndo();
            return sub;
        }

        private static void AppendToItems(
            AdapterRuntimeSettingsCollectionSO collection,
            params AdapterRuntimeSettingsBase[] subs)
        {
            var so = new SerializedObject(collection);
            var items = so.FindProperty("_items");
            var startIndex = items.arraySize;
            items.arraySize = startIndex + subs.Length;
            for (var i = 0; i < subs.Length; i++)
            {
                items.GetArrayElementAtIndex(startIndex + i).objectReferenceValue = subs[i];
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void RemoveSubAssetFromCollection(
            AdapterRuntimeSettingsCollectionSO collection,
            AdapterRuntimeSettingsBase target)
        {
            var index = collection.IndexOf(target);
            Assert.GreaterOrEqual(index, 0, "削除対象が _items に存在する必要があります。");

            var so = new SerializedObject(collection);
            var items = so.FindProperty("_items");
            items.DeleteArrayElementAtIndex(index);
            // DeleteArrayElementAtIndex で参照型は最初 null 化されることがあるため、再評価する。
            if (items.arraySize > index
                && items.GetArrayElementAtIndex(index).objectReferenceValue == null)
            {
                items.DeleteArrayElementAtIndex(index);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            AssetDatabase.RemoveObjectFromAsset(target);
            Object.DestroyImmediate(target, allowDestroyingAssets: false);
        }
    }

    /// <summary>テスト用の <see cref="AdapterRuntimeSettingsBase"/> 派生型（型検索の識別用、中身は空）。</summary>
    public sealed class FakeAlphaSettings : AdapterRuntimeSettingsBase
    {
    }

    /// <summary>テスト用の <see cref="AdapterRuntimeSettingsBase"/> 派生型（型検索の識別用、中身は空）。</summary>
    public sealed class FakeBetaSettings : AdapterRuntimeSettingsBase
    {
    }

    /// <summary>テスト用の <see cref="AdapterRuntimeSettingsBase"/> 派生型（sub-asset 3 件目の識別用、中身は空）。</summary>
    public sealed class FakeGammaSettings : AdapterRuntimeSettingsBase
    {
    }
}
