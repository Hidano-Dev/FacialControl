using System.Collections.Generic;
using Hidano.FacialControl.Editor.Common;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor
{
    [TestFixture]
    [SmallTest]
    public class ListViewFoldoutStatePersistenceTests : SizedTestFixture
    {
        private FoldoutHostSO _host;
        private SerializedObject _serializedObject;
        private readonly List<string> _usedKeys = new List<string>();

        [SetUp]
        public void SetUp()
        {
            _host = ScriptableObject.CreateInstance<FoldoutHostSO>();
            _serializedObject = new SerializedObject(_host);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (string key in _usedKeys)
            {
                SessionState.EraseBool(key);
            }
            _usedKeys.Clear();

            _serializedObject?.Dispose();
            _serializedObject = null;

            if (_host != null)
            {
                Object.DestroyImmediate(_host);
                _host = null;
            }
        }

        [Test]
        public void Register_NoSavedState_KeepsDefaultOpen()
        {
            SerializedProperty listProperty = FindItemsProperty();
            TrackKey(listProperty);
            var listView = CreateFoldoutListView();

            ListViewFoldoutStatePersistence.Register(listView, listProperty);

            Foldout foldout = FindHeaderFoldout(listView);
            Assert.That(foldout, Is.Not.Null);
            Assert.That(foldout.value, Is.True,
                "保存済み状態が無い場合は既定（展開）のままである必要があります。");
        }

        [Test]
        public void Register_SavedCollapsedState_RestoresCollapsed()
        {
            SerializedProperty listProperty = FindItemsProperty();
            string key = TrackKey(listProperty);
            SessionState.SetBool(key, false);

            var listView = CreateFoldoutListView();
            ListViewFoldoutStatePersistence.Register(listView, listProperty);

            Foldout foldout = FindHeaderFoldout(listView);
            Assert.That(foldout.value, Is.False,
                "SessionState に折りたたみ状態が保存されている場合、生成時に復元される必要があります。");
        }

        [Test]
        public void SaveState_CollapsedFoldout_PersistsAndRestoresOnNextRegister()
        {
            // ChangeEvent は panel 未接続時に発火しないため、detach 経路が呼ぶ SaveState を
            // 直接検証する（保存 → 再生成で復元のラウンドトリップ）。
            SerializedProperty listProperty = FindItemsProperty();
            string key = TrackKey(listProperty);

            var listView = CreateFoldoutListView();
            ListViewFoldoutStatePersistence.Register(listView, listProperty);
            FindHeaderFoldout(listView).value = false;
            ListViewFoldoutStatePersistence.SaveState(listView, listProperty);

            Assert.That(SessionState.GetBool(key, true), Is.False,
                "SaveState で現在の折りたたみ状態が SessionState へ保存される必要があります。");

            var rebuiltListView = CreateFoldoutListView();
            ListViewFoldoutStatePersistence.Register(rebuiltListView, listProperty);
            Assert.That(FindHeaderFoldout(rebuiltListView).value, Is.False,
                "再生成した ListView に直前の折りたたみ状態が復元される必要があります。");
        }

        [Test]
        public void SaveState_KeyOverloadAfterSerializedObjectDisposed_PersistsWithoutError()
        {
            // 回帰テスト: Inspector を別オブジェクトへ切り替えると DetachFromPanelEvent 時点で
            // SerializedObject が破棄済みのことがあり、キー再計算（targetObject アクセス）で
            // NullReferenceException が発生していた。detach 経路は Register 時に確定済みの
            // キーを受け取る overload を使い、破棄後も保存できる必要がある。
            SerializedProperty listProperty = FindItemsProperty();
            string key = TrackKey(listProperty);

            var listView = CreateFoldoutListView();
            ListViewFoldoutStatePersistence.Register(listView, listProperty);
            FindHeaderFoldout(listView).value = false;

            _serializedObject.Dispose();

            Assert.DoesNotThrow(
                () => ListViewFoldoutStatePersistence.SaveState(listView, key),
                "SerializedObject 破棄後でもキー指定の SaveState は例外を出さず保存できる必要があります。");
            Assert.That(SessionState.GetBool(key, true), Is.False,
                "破棄後の SaveState でも折りたたみ状態が SessionState へ保存される必要があります。");
        }

        [Test]
        public void GetSessionStateKey_DifferentProperties_ProduceDifferentKeys()
        {
            SerializedProperty itemsProperty = FindItemsProperty();
            SerializedProperty otherProperty = _serializedObject.FindProperty(
                nameof(FoldoutHostSO.OtherItems));
            Assert.That(otherProperty, Is.Not.Null);

            Assert.That(
                ListViewFoldoutStatePersistence.GetSessionStateKey(itemsProperty),
                Is.Not.EqualTo(ListViewFoldoutStatePersistence.GetSessionStateKey(otherProperty)),
                "同一オブジェクト内でもプロパティごとに保存キーが分かれる必要があります。");
        }

        private SerializedProperty FindItemsProperty()
        {
            SerializedProperty property = _serializedObject.FindProperty(nameof(FoldoutHostSO.Items));
            Assert.That(property, Is.Not.Null);
            return property;
        }

        private string TrackKey(SerializedProperty listProperty)
        {
            string key = ListViewFoldoutStatePersistence.GetSessionStateKey(listProperty);
            _usedKeys.Add(key);
            return key;
        }

        private static ListView CreateFoldoutListView()
        {
            return new ListView
            {
                showFoldoutHeader = true,
                headerTitle = "テストリスト",
            };
        }

        private static Foldout FindHeaderFoldout(ListView listView)
        {
            return listView.Q<Foldout>(className: BaseListView.foldoutHeaderUssClassName);
        }

        private sealed class FoldoutHostSO : ScriptableObject
        {
            public List<int> Items = new List<int>();
            public List<int> OtherItems = new List<int>();
        }
    }
}
