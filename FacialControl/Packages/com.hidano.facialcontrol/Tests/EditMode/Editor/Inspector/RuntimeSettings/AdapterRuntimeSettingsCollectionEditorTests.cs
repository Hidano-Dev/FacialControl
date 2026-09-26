using System.IO;
using Hidano.FacialControl.Adapters.RuntimeSettings;
using Hidano.FacialControl.Editor.Inspector.RuntimeSettings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector.RuntimeSettings
{
    /// <summary>
    /// <see cref="AdapterRuntimeSettingsCollectionEditor"/> の Add/Remove API 経由で
    /// AssetDatabase 上に sub-asset が生成/除去されること、および同型・同 <c>_label</c> の
    /// 重複追加がブロックされないこと (要件 3.1, 3.3, 6.3, 6.4, 6.8) を検証する。
    /// </summary>
    [TestFixture]
    public class AdapterRuntimeSettingsCollectionEditorTests
    {
        public sealed class FakeAlphaEditorSettings : AdapterRuntimeSettingsBase
        {
        }

        public sealed class FakeBetaEditorSettings : AdapterRuntimeSettingsBase
        {
        }

        private const string TempFolderName = "_TestAdapterRuntimeSettingsCollectionEditor";
        private const string TempAssetsRoot = "Assets/" + TempFolderName;

        private string _collectionAssetPath;
        private AdapterRuntimeSettingsCollectionSO _collection;
        private AdapterRuntimeSettingsCollectionEditor _editor;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempAssetsRoot))
            {
                AssetDatabase.CreateFolder("Assets", TempFolderName);
            }

            string randomSuffix = Path.GetRandomFileName().Replace(".", string.Empty);
            _collectionAssetPath = $"{TempAssetsRoot}/Collection_{randomSuffix}.asset";

            _collection = ScriptableObject.CreateInstance<AdapterRuntimeSettingsCollectionSO>();
            AssetDatabase.CreateAsset(_collection, _collectionAssetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(_collectionAssetPath);

            _editor = (AdapterRuntimeSettingsCollectionEditor)UnityEditor.Editor.CreateEditor(_collection);
            Assert.IsNotNull(_editor,
                "CreateEditor が AdapterRuntimeSettingsCollectionEditor を返さなかった (CustomEditor 属性未解決)。");
            _editor.CreateInspectorGUI();

            AdapterRuntimeSettingsCollectionEditor.SuppressRemoveConfirmation = true;
        }

        [TearDown]
        public void TearDown()
        {
            AdapterRuntimeSettingsCollectionEditor.SuppressRemoveConfirmation = false;

            if (_editor != null)
            {
                Object.DestroyImmediate(_editor);
                _editor = null;
            }

            if (!string.IsNullOrEmpty(_collectionAssetPath))
            {
                AssetDatabase.DeleteAsset(_collectionAssetPath);
            }
            if (AssetDatabase.IsValidFolder(TempAssetsRoot))
            {
                AssetDatabase.DeleteAsset(TempAssetsRoot);
            }
            AssetDatabase.Refresh();

            _collection = null;
            _collectionAssetPath = null;
        }

        [Test]
        public void AddSubAssetOfType_ValidConcreteType_AddsItemAndPersistsAsSubAsset()
        {
            _editor.AddSubAssetOfType(typeof(FakeAlphaEditorSettings));

            Assert.AreEqual(1, _collection.Items.Count,
                "Add 操作後に _items 数が 1 になっていない。");

            var added = _collection.Items[0];
            Assert.IsNotNull(added, "追加された sub-asset が null。");
            Assert.IsInstanceOf<FakeAlphaEditorSettings>(added);

            string subPath = AssetDatabase.GetAssetPath(added);
            Assert.AreEqual(_collectionAssetPath, subPath,
                "Add された sub-asset の AssetPath が Collection asset と一致しない。");
            Assert.IsTrue(AssetDatabase.IsSubAsset(added),
                "Add された sub-asset が AssetDatabase 上 sub-asset として扱われていない。");
        }

        [Test]
        public void AddSubAssetOfType_TwoDifferentTypes_PreservesBothEntries()
        {
            _editor.AddSubAssetOfType(typeof(FakeAlphaEditorSettings));
            _editor.AddSubAssetOfType(typeof(FakeBetaEditorSettings));

            Assert.AreEqual(2, _collection.Items.Count);
            Assert.IsInstanceOf<FakeAlphaEditorSettings>(_collection.Items[0]);
            Assert.IsInstanceOf<FakeBetaEditorSettings>(_collection.Items[1]);
        }

        [Test]
        public void RemoveSubAssetAt_MiddleIndex_RemovesOnlyTargetSubAsset()
        {
            _editor.AddSubAssetOfType(typeof(FakeAlphaEditorSettings));
            _editor.AddSubAssetOfType(typeof(FakeBetaEditorSettings));
            _editor.AddSubAssetOfType(typeof(FakeAlphaEditorSettings));

            Assert.AreEqual(3, _collection.Items.Count,
                "Add 3 回後に _items 数が 3 になっていない。");

            var first = _collection.Items[0];
            var last = _collection.Items[2];
            string firstSubPath = AssetDatabase.GetAssetPath(first);
            string lastSubPath = AssetDatabase.GetAssetPath(last);

            _editor.RemoveSubAssetAt(1);

            Assert.AreEqual(2, _collection.Items.Count,
                "Remove 後の _items 数が 2 になっていない。");
            Assert.AreSame(first, _collection.Items[0],
                "先頭要素が Remove 操作で消えてはいけない。");
            Assert.AreSame(last, _collection.Items[1],
                "末尾要素が Remove 操作で消えてはいけない。");

            Assert.AreEqual(_collectionAssetPath, firstSubPath,
                "Remove 後も先頭 sub-asset は Collection asset 直下に残っている必要がある。");
            Assert.AreEqual(_collectionAssetPath, lastSubPath,
                "Remove 後も末尾 sub-asset は Collection asset 直下に残っている必要がある。");
        }

        [Test]
        public void AddSubAssetOfType_DuplicateLabelOfSameType_StillAddsItem()
        {
            // 同型・同 _label の重複は警告ログのみで、追加自体はブロックしない（要件 6.8）。
            // 警告ログの文言はテスト対象にしない。
            _editor.AddSubAssetOfType(typeof(FakeAlphaEditorSettings));

            _editor.AddSubAssetOfType(typeof(FakeAlphaEditorSettings));

            Assert.AreEqual(2, _collection.Items.Count,
                "_label 重複でも sub-asset の追加自体は許可されるべき (要件 6.8)。");
        }
    }
}
