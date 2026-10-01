using System;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Inspector.AdapterBindings
{
    // ---------------------------------------------------------------
    // Mock 型定義
    // namespace scope に置いて FQTN を安定化させる（[SerializeReference]
    // round-trip の concrete type 解決に必要）。
    // ---------------------------------------------------------------

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_AAA_Simple")]
    public sealed class MockListViewSimpleBinding : AdapterBindingBase { }

    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_CCC_ThrowingDrawer")]
    public sealed class MockListViewThrowingDrawerBinding : AdapterBindingBase { }

    /// <summary>
    /// SerializeField を持つ binding。AddBindingFromDescriptor が SerializedProperty 経由で
    /// 追加していないと、子要素 `_settingsObject` の SerializedProperty が初回 Drawer 描画時に
    /// null となり、PropertyField が DOM に生成されない現象 (= 「スロットが出ない」) を再現する。
    /// </summary>
    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_DDD_HasSerializedField")]
    public sealed class MockListViewWithSerializedField : AdapterBindingBase
    {
        [SerializeField]
        public UnityEngine.Object _settingsObject;
    }

    /// <summary>
    /// CreatePropertyGUI 内で例外を投げる PropertyDrawer。ListView が 1 行の Drawer 例外で
    /// 全体の構築を止めないことを確認するために使う。
    /// </summary>
    [CustomPropertyDrawer(typeof(MockListViewThrowingDrawerBinding))]
    public sealed class MockListViewThrowingDrawerBindingDrawer : PropertyDrawer
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            throw new InvalidOperationException(
                "Intentional PropertyDrawer exception from MockListViewThrowingDrawerBindingDrawer");
        }
    }

    /// <summary>
    /// MockListViewWithSerializedField 用 Drawer。
    /// `property.FindPropertyRelative("_settingsObject")` で子 SerializedProperty を取得し
    /// PropertyField を生成する。SerializeReference 追加直後でも子 SerializedProperty が
    /// 解決できれば PropertyField が DOM に配置される。
    /// </summary>
    [CustomPropertyDrawer(typeof(MockListViewWithSerializedField))]
    public sealed class MockListViewWithSerializedFieldDrawer : PropertyDrawer
    {
        public const string SettingsFieldElementName = "mock-listview-settings-field";

        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            var root = new VisualElement();
            SerializedProperty settingsProp = property.FindPropertyRelative("_settingsObject");
            if (settingsProp == null)
            {
                root.Add(new Label("<missing field: _settingsObject>"));
                return root;
            }
            root.Add(new PropertyField(settingsProp, "Settings Object")
            {
                name = SettingsFieldElementName,
            });
            return root;
        }
    }

    /// <summary>
    /// ヘッダー要約を提供する binding。<see cref="MockListViewSummaryBindingDrawer"/> が
    /// <see cref="IAdapterBindingHeaderSummaryProvider"/> を実装し、<c>_port</c> を要約にする（0 なら要約なし）。
    /// </summary>
    [Serializable]
    [FacialAdapterBinding(displayName: "ZZZ_ListViewTest_EEE_HeaderSummary")]
    public sealed class MockListViewSummaryBinding : AdapterBindingBase
    {
        [SerializeField]
        public int _port;
    }

    [CustomPropertyDrawer(typeof(MockListViewSummaryBinding))]
    public sealed class MockListViewSummaryBindingDrawer : PropertyDrawer, IAdapterBindingHeaderSummaryProvider
    {
        public override VisualElement CreatePropertyGUI(SerializedProperty property)
        {
            return new VisualElement();
        }

        public AdapterBindingHeaderSummary GetHeaderSummary(SerializedProperty property)
        {
            int port = property.FindPropertyRelative("_port").intValue;
            return port == 0
                ? AdapterBindingHeaderSummary.None
                : new AdapterBindingHeaderSummary(":" + port, "受信ポート: " + port);
        }
    }

    /// <summary>
    /// <see cref="AdapterBindingsListView"/> の smoke テスト。
    /// 「null 要素 / 例外を投げる Drawer を含んでも構築できる」「Add 操作が SerializedObject へ書き込まれる」
    /// 「SerializeReference 追加直後に Drawer の PropertyField が出る（スロットが出ない不具合の回帰）」、
    /// 「Foldout の開閉状態が要素単位に保持される（削除・並べ替えでずれない）」、
    /// 「Drawer が要約を提供すればヘッダーに出し、提供しなければ表示名だけにする」を守る。
    /// slug 重複の検出は <c>FacialCharacterProfileAssetGuardTests</c> 側で保証する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AdapterBindingsListViewTests : SizedTestFixture
    {
        private const string TempFolderParent = "Assets";
        private const string TempFolderName = "__Temp_AdapterBindingsListViewTests";
        private static readonly string TempFolderPath = TempFolderParent + "/" + TempFolderName;

        private string _assetPath;
        private TestFacialCharacterProfileSO _so;
        private SerializedObject _serializedObject;
        private SerializedProperty _listProperty;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(TempFolderPath))
            {
                AssetDatabase.CreateFolder(TempFolderParent, TempFolderName);
            }

            _assetPath = TempFolderPath + "/AdapterBindingsListViewTests_" + Guid.NewGuid().ToString("N") + ".asset";
            _so = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            AssetDatabase.CreateAsset(_so, _assetPath);
            AssetDatabase.SaveAssets();

            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");
            Assert.IsNotNull(_listProperty, "_adapterBindings SerializedProperty が解決できない。");
        }

        [TearDown]
        public void TearDown()
        {
            EraseFoldoutStates();
            _serializedObject = null;
            _listProperty = null;
            _so = null;

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

        private void ReloadSerializedObject()
        {
            EditorUtility.SetDirty(_so);
            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");
        }

        private void EraseFoldoutStates()
        {
            if (_so == null) return;
            var so = new SerializedObject(_so);
            var list = so.FindProperty("_adapterBindings");
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                string key = AdapterBindingFoldoutState.GetSessionStateKey(list.GetArrayElementAtIndex(i));
                if (!string.IsNullOrEmpty(key)) SessionState.EraseBool(key);
            }
        }

        private string GetKey(int index)
        {
            return AdapterBindingFoldoutState.GetSessionStateKey(_listProperty.GetArrayElementAtIndex(index));
        }

        private static AdapterBindingDescriptor RequireDescriptor(Type concreteType)
        {
            var descriptor = AdapterBindingDiscovery.FindByType(concreteType);
            Assert.IsTrue(descriptor.HasValue,
                $"AdapterBindingDiscovery.FindByType({concreteType.FullName}) は non-null を返さなければならない。");
            return descriptor.Value;
        }

        // ---------------------------------------------------------------
        // smoke 1: 生成できる（null 要素・Drawer 例外を含んでも例外を外へ出さない）
        // ---------------------------------------------------------------

        [Test]
        public void Construct_WithNullElementAndThrowingDrawer_DoesNotThrow()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "ok-front" });
            _so.WritableAdapterBindings.Add(null);
            _so.WritableAdapterBindings.Add(new MockListViewThrowingDrawerBinding { Slug = "boom" });
            EditorUtility.SetDirty(_so);
            _serializedObject = new SerializedObject(_so);
            _listProperty = _serializedObject.FindProperty("_adapterBindings");

            AdapterBindingsListView view = null;
            Assert.DoesNotThrow(() => view = new AdapterBindingsListView(_listProperty));

            Assert.IsNotNull(view);
        }

        // ---------------------------------------------------------------
        // smoke 2: 保存が通る（Add → SerializedObject へ書き込み）
        // ---------------------------------------------------------------

        [Test]
        public void AddBindingFromDescriptor_AppendsConcreteInstanceWithSlugAutoPopulated()
        {
            var view = new AdapterBindingsListView(_listProperty);
            var descriptor = RequireDescriptor(typeof(MockListViewSimpleBinding));

            view.AddBindingFromDescriptor(descriptor);

            _serializedObject.Update();
            Assert.AreEqual(1, _listProperty.arraySize,
                "AddBindingFromDescriptor 後に _adapterBindings の要素数は 1 になるべき。");

            var element = _listProperty.GetArrayElementAtIndex(0);
            Assert.IsInstanceOf<MockListViewSimpleBinding>(element.managedReferenceValue,
                "Add 後の要素は descriptor.Type と同じ concrete type であるべき。");

            var expectedSlug = AdapterSlug.FromDisplayName(descriptor.OriginalDisplayName).Value;
            var added = (MockListViewSimpleBinding)element.managedReferenceValue;
            Assert.AreEqual(expectedSlug, added.Slug,
                "Slug は AdapterSlug.FromDisplayName(displayName) で auto-populate されるべき。");
        }

        // ---------------------------------------------------------------
        // 回帰: SerializeReference 追加直後の Drawer 描画（スロットが出ない不具合）
        // ---------------------------------------------------------------

        [Test]
        public void AddBindingFromDescriptor_DrawerWithSerializedField_CreatesPropertyFieldInRowImmediately()
        {
            // AdapterBindingsListView.AddBindingFromDescriptor が SerializedProperty 経由で
            // 追加していない場合 (古い list.Add(instance) 経路) は、子 SerializedProperty
            // `_settingsObject` が初回 Rebuild 時に解決できず、Drawer は missing label を出して
            // PropertyField (= ObjectField のスロット) が DOM に生成されない。
            var view = new AdapterBindingsListView(_listProperty);
            var descriptor = RequireDescriptor(typeof(MockListViewWithSerializedField));

            view.AddBindingFromDescriptor(descriptor);

            var propertyField = view.Q<PropertyField>(name: MockListViewWithSerializedFieldDrawer.SettingsFieldElementName);
            Assert.IsNotNull(propertyField,
                "SerializeReference 追加直後でも Drawer の PropertyField が DOM に存在するべき。");
        }

        // ---------------------------------------------------------------
        // Foldout: 開閉状態の保持（UI ツリーではなく保存先の状態で検証する）
        // ---------------------------------------------------------------

        [Test]
        public void Construct_WithNullElementAndThrowingDrawerAndFoldoutState_DoesNotThrow()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "ok-front" });
            _so.WritableAdapterBindings.Add(null);
            _so.WritableAdapterBindings.Add(new MockListViewThrowingDrawerBinding { Slug = "boom" });
            ReloadSerializedObject();
            AdapterBindingFoldoutState.Save(GetKey(0), false);

            AdapterBindingsListView view = null;
            Assert.DoesNotThrow(() => view = new AdapterBindingsListView(_listProperty));
            Assert.DoesNotThrow(() => view.SetAllExpanded(true));
        }

        [Test]
        public void Load_NoSavedState_ReturnsExpanded()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "a" });
            ReloadSerializedObject();

            Assert.IsTrue(AdapterBindingFoldoutState.Load(GetKey(0)), "保存済みの開閉状態がなければ展開とするべき。");
        }

        [Test]
        public void SetAllExpanded_False_SavesCollapsedStateForEveryElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "a" });
            _so.WritableAdapterBindings.Add(new MockListViewWithSerializedField { Slug = "b" });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);
            view.SetAllExpanded(false);

            Assert.IsFalse(AdapterBindingFoldoutState.Load(GetKey(0)));
            Assert.IsFalse(AdapterBindingFoldoutState.Load(GetKey(1)));
        }

        [Test]
        public void RemoveBindingAt_AfterCollapsingLaterElement_StateStaysWithSameElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewWithSerializedField { Slug = "second" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "third" });
            ReloadSerializedObject();
            string secondKey = GetKey(1);
            AdapterBindingFoldoutState.Save(secondKey, false);

            var view = new AdapterBindingsListView(_listProperty);
            view.RemoveBindingAt(0);
            ReloadSerializedObject();

            Assert.AreEqual(secondKey, GetKey(0), "削除で index がずれても、要素の保存キーは変わらないべき。");
            Assert.IsFalse(AdapterBindingFoldoutState.Load(GetKey(0)), "折り畳んだ要素の状態が維持されるべき。");
            Assert.IsTrue(AdapterBindingFoldoutState.Load(GetKey(1)), "折り畳んでいない要素に状態が移ってはならない。");
        }

        [Test]
        public void Reorder_CollapsedElementMoved_StateFollowsElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewWithSerializedField { Slug = "second" });
            ReloadSerializedObject();

            string firstKey = GetKey(0);
            AdapterBindingFoldoutState.Save(firstKey, false);

            var bindings = _so.WritableAdapterBindings;
            (bindings[0], bindings[1]) = (bindings[1], bindings[0]);
            ReloadSerializedObject();

            Assert.AreEqual(firstKey, GetKey(1), "並べ替え後も要素の保存キーは変わらないべき。");
            Assert.IsFalse(AdapterBindingFoldoutState.Load(GetKey(1)), "並べ替えで移動した要素に折り畳み状態が付いて回るべき。");
            Assert.IsTrue(AdapterBindingFoldoutState.Load(GetKey(0)), "入れ替わった先の要素に状態が移ってはならない。");
        }

        [Test]
        public void GetSessionStateKey_DifferentElements_ProduceDifferentKeys()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "a" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "b" });
            _so.WritableAdapterBindings.Add(null);
            ReloadSerializedObject();

            string key0 = AdapterBindingFoldoutState.GetSessionStateKey(_listProperty.GetArrayElementAtIndex(0));
            string key1 = AdapterBindingFoldoutState.GetSessionStateKey(_listProperty.GetArrayElementAtIndex(1));
            string keyNull = AdapterBindingFoldoutState.GetSessionStateKey(_listProperty.GetArrayElementAtIndex(2));

            Assert.IsNotNull(key0);
            Assert.IsNotNull(key1);
            Assert.AreNotEqual(key0, key1, "別の要素は別のキーを持つべき。");
            Assert.IsNull(keyNull, "null 要素は参照 ID を持たないためキーを作らない。");
        }

        // ---------------------------------------------------------------
        // Foldout ヘッダーの要約（IAdapterBindingHeaderSummaryProvider）
        // ---------------------------------------------------------------

        [Test]
        public void Construct_DrawerProvidesHeaderSummary_ShowsSummaryInFoldoutHeader()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSummaryBinding { Slug = "osc", _port = 9001 });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var foldout = view.Q<Foldout>(className: AdapterBindingsListView.RowFoldoutClassName);
            var summary = foldout.Q<Label>(className: AdapterBindingsListView.HeaderSummaryClassName);
            Assert.IsNotNull(summary, "要約を提供する Drawer の行はヘッダーに要約ラベルを持つべき。");
            Assert.AreEqual(":9001", summary.text);
            Assert.AreEqual("受信ポート: 9001", summary.tooltip);
            Assert.AreEqual(DisplayStyle.Flex, summary.style.display.value);
            Assert.IsTrue(foldout.Q(className: Foldout.inputUssClassName).Contains(summary),
                "要約は折り畳んでも見えるよう Foldout のヘッダー側に置くべき。");
        }

        [Test]
        public void Construct_ProviderReturnsNone_HidesSummaryLabel()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSummaryBinding { Slug = "osc", _port = 0 });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var summary = view.Q<Label>(className: AdapterBindingsListView.HeaderSummaryClassName);
            Assert.IsNotNull(summary);
            Assert.AreEqual(DisplayStyle.None, summary.style.display.value);
        }

        [Test]
        public void Construct_DrawerWithoutSummaryProvider_HasNoSummaryLabel()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "plain" });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            Assert.IsNull(view.Q<Label>(className: AdapterBindingsListView.HeaderSummaryClassName),
                "要約を提供しない binding のヘッダーは従来どおり表示名と slug だけにする。");
        }
    }
}
