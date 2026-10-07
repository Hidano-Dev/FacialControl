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
    /// 「Drawer が要約を提供すればヘッダーに出し、提供しなければ表示名だけにする」、
    /// 「ヘッダーの有効トグルで binding を無効にでき、設定値は残る」、
    /// 「ヘッダーの ▲ / ▼ で並び順を入れ替えられ、Undo で戻せ、開閉状態は要素に付いて回る」を守る。
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

        // ---------------------------------------------------------------
        // Foldout ヘッダーの有効 / 無効トグル
        // ---------------------------------------------------------------

        [Test]
        public void Construct_DefaultBinding_HeaderToggleIsOnInFoldoutHeader()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "plain" });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var foldout = view.Q<Foldout>(className: AdapterBindingsListView.RowFoldoutClassName);
            var toggle = foldout.Q<Toggle>(className: AdapterBindingsListView.HeaderEnabledToggleClassName);
            Assert.IsNotNull(toggle, "折り畳んだままでも切り替えられるよう、トグルはヘッダーに置くべき。");
            Assert.IsTrue(foldout.Q(className: Foldout.inputUssClassName).Contains(toggle));
            Assert.IsTrue(toggle.value, "既存 binding は既定で有効。");
            Assert.IsNull(view.Q(className: AdapterBindingsListView.DisabledRowClassName));
        }

        [Test]
        public void Construct_DisabledBinding_ToggleOffAndRowMarkedDisabled()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "off", Disabled = true });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var toggle = view.Q<Toggle>(className: AdapterBindingsListView.HeaderEnabledToggleClassName);
            Assert.IsFalse(toggle.value);
            Assert.IsNotNull(view.Q(className: AdapterBindingsListView.DisabledRowClassName),
                "無効の行はヘッダーの見た目で分かるようにする。");
        }

        [Test]
        public void SetBindingEnabled_OffThenOn_WritesDisabledAndKeepsSettings()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSummaryBinding { Slug = "osc", _port = 9001 });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);

            view.SetBindingEnabled(0, false);

            Assert.IsTrue(_so.AdapterBindings[0].Disabled, "トグルを切ると SO の binding が無効になる。");
            Assert.AreEqual(9001, ((MockListViewSummaryBinding)_so.AdapterBindings[0])._port, "設定値は保持する。");
            Assert.IsFalse(view.Q<Toggle>(className: AdapterBindingsListView.HeaderEnabledToggleClassName).value);
            Assert.IsNotNull(view.Q(className: AdapterBindingsListView.DisabledRowClassName));

            view.SetBindingEnabled(0, true);

            Assert.IsFalse(_so.AdapterBindings[0].Disabled);
            Assert.AreEqual(9001, ((MockListViewSummaryBinding)_so.AdapterBindings[0])._port);
            Assert.IsTrue(view.Q<Toggle>(className: AdapterBindingsListView.HeaderEnabledToggleClassName).value);
            Assert.IsNull(view.Q(className: AdapterBindingsListView.DisabledRowClassName));
        }

        [Test]
        public void Construct_BindingWithoutDrawer_BodyOmitsDisabledField()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "plain" });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var fields = view.Query<PropertyField>().ToList();
            Assert.IsTrue(fields.Exists(f => f.bindingPath.EndsWith("." + nameof(AdapterBindingBase.Slug))),
                "Drawer の無い binding の本文には従来どおり子プロパティを並べる。");
            Assert.IsFalse(fields.Exists(f => f.bindingPath.EndsWith("." + nameof(AdapterBindingBase.Disabled))),
                "ヘッダーのトグルと重複する Disabled は本文に出さない。");
        }

        [Test]
        public void Construct_NullElement_HasNoHeaderToggle()
        {
            _so.WritableAdapterBindings.Add(null);
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            Assert.IsNull(view.Q<Toggle>(className: AdapterBindingsListView.HeaderEnabledToggleClassName),
                "型欠落の行は起動対象にならないためトグルを出さない。");
        }

        // ---------------------------------------------------------------
        // Foldout ヘッダーの ▲ / ▼（並び替え）
        // ---------------------------------------------------------------

        private string[] GetSlugs()
        {
            var bindings = _so.AdapterBindings;
            var slugs = new string[bindings.Count];
            for (int i = 0; i < bindings.Count; i++)
            {
                slugs[i] = bindings[i]?.Slug;
            }
            return slugs;
        }

        [Test]
        public void MoveBinding_FirstToSecond_SwapsOrderInProfile()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewWithSerializedField { Slug = "second" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "third" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);

            Assert.IsTrue(view.MoveBinding(0, 1));

            CollectionAssert.AreEqual(new[] { "second", "first", "third" }, GetSlugs(),
                "移動はプロファイルの _adapterBindings の順序に書き込まれるべき。");
            Assert.IsInstanceOf<MockListViewWithSerializedField>(_so.AdapterBindings[0],
                "要素は型と設定ごと移動するべき。");
        }

        [Test]
        public void MoveBinding_OutOfRangeOrSameIndex_ReturnsFalseAndKeepsOrder()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "second" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);

            Assert.IsFalse(view.MoveBinding(0, -1), "先頭より上へは移動しない。");
            Assert.IsFalse(view.MoveBinding(1, 2), "末尾より下へは移動しない。");
            Assert.IsFalse(view.MoveBinding(1, 1));

            CollectionAssert.AreEqual(new[] { "first", "second" }, GetSlugs());
        }

        [Test]
        public void MoveBinding_ThenUndo_RestoresOriginalOrder()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "second" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);
            // 移動が Undo に記録されなかった場合に、無関係な過去の操作を戻して誤判定しないよう履歴を空にする。
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();

            view.MoveBinding(1, 0);
            CollectionAssert.AreEqual(new[] { "second", "first" }, GetSlugs());

            Undo.PerformUndo();

            CollectionAssert.AreEqual(new[] { "first", "second" }, GetSlugs(), "移動は Undo で元に戻せるべき。");
        }

        [Test]
        public void MoveBinding_SelectedOrNeighborMoved_SelectionFollowsSameElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "second" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "third" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);
            view.SelectBinding(1);

            view.MoveBinding(1, 2);
            Assert.AreEqual(2, view.SelectedIndex, "選択した要素を動かしたら選択も移動先へ付いて行くべき。");

            view.MoveBinding(0, 2);
            Assert.AreEqual(1, view.SelectedIndex, "間の要素を動かして index がずれても、同じ要素を選択し続けるべき。");
            Assert.AreEqual("second", _so.AdapterBindings[view.SelectedIndex].Slug);
        }

        [Test]
        public void MoveBinding_NullElementSelectedAndNeighborMoved_SelectionFollowsNullElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "a" });
            _so.WritableAdapterBindings.Add(null);
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "b" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);
            view.SelectBinding(1);

            view.MoveBinding(0, 1);

            Assert.AreEqual(0, view.SelectedIndex,
                "参照 ID の無い null 要素の選択も、隣の要素の移動で別の Adapter に移ってはならない。");
            Assert.IsNull(_so.AdapterBindings[view.SelectedIndex]);
        }

        [Test]
        public void SelectBinding_NullElementThenUndoRedo_ClearsSelection()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "a" });
            _so.WritableAdapterBindings.Add(null);
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);
            view.SelectBinding(1);

            // Undo / Redo の後は index だけで持つ null 要素の選択を追えないため、選択を外す。
            view.ClearIndexOnlySelection();

            Assert.AreEqual(-1, view.SelectedIndex,
                "null 要素の選択は Undo / Redo で外し、別の Adapter を削除対象にしてはならない。");
        }

        [Test]
        public void SelectBinding_ReferenceElementThenUndoRedo_KeepsSelection()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "a" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);
            view.SelectBinding(0);

            view.ClearIndexOnlySelection();

            Assert.AreEqual(0, view.SelectedIndex, "参照 ID で持つ選択は Undo / Redo でも保つ。");
        }

        [Test]
        public void MoveBinding_ThenUndo_SelectionStaysOnSameElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "second" });
            ReloadSerializedObject();
            var view = new AdapterBindingsListView(_listProperty);
            view.SelectBinding(0);
            Undo.ClearAll();
            Undo.IncrementCurrentGroup();
            view.MoveBinding(0, 1);

            Undo.PerformUndo();
            CollectionAssert.AreEqual(new[] { "first", "second" }, GetSlugs());
            // Undo 後の次の操作（ここでは別の要素の移動）で、選択が index ではなく要素に付いていることを確かめる。
            view.MoveBinding(1, 0);

            Assert.AreEqual("first", _so.AdapterBindings[view.SelectedIndex].Slug,
                "移動を Undo しても、選択は最初に選んだ要素を指し続けるべき（別の Adapter を削除対象にしない）。");
        }

        [Test]
        public void MoveBinding_CollapsedElementMoved_FoldoutStateFollowsElement()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "second" });
            ReloadSerializedObject();
            string firstKey = GetKey(0);
            AdapterBindingFoldoutState.Save(firstKey, false);
            var view = new AdapterBindingsListView(_listProperty);

            view.MoveBinding(0, 1);
            ReloadSerializedObject();

            Assert.AreEqual(firstKey, GetKey(1), "移動後も要素の保存キーは変わらないべき。");
            Assert.IsFalse(AdapterBindingFoldoutState.Load(GetKey(1)), "折り畳んだ要素の状態が移動先でも維持されるべき。");
            Assert.IsTrue(AdapterBindingFoldoutState.Load(GetKey(0)), "入れ替わった要素に状態が移ってはならない。");
            var foldouts = view.Query<Foldout>(className: AdapterBindingsListView.RowFoldoutClassName).ToList();
            Assert.IsTrue(foldouts[0].value, "作り直した行でも、展開していた要素は展開のまま。");
            Assert.IsFalse(foldouts[1].value, "作り直した行でも、折り畳んだ要素は折り畳んだまま。");
        }

        [Test]
        public void Construct_ThreeBindings_MoveButtonsInHeaderAndDisabledAtEnds()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "first" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "second" });
            _so.WritableAdapterBindings.Add(new MockListViewSimpleBinding { Slug = "third" });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var foldouts = view.Query<Foldout>(className: AdapterBindingsListView.RowFoldoutClassName).ToList();
            Assert.AreEqual(3, foldouts.Count);
            bool[] expectedUp = { false, true, true };
            bool[] expectedDown = { true, true, false };
            for (int i = 0; i < foldouts.Count; i++)
            {
                var header = foldouts[i].Q(className: Foldout.inputUssClassName);
                var up = header.Q<Button>(className: AdapterBindingsListView.HeaderMoveUpButtonClassName);
                var down = header.Q<Button>(className: AdapterBindingsListView.HeaderMoveDownButtonClassName);
                Assert.IsNotNull(up, "折り畳んだままでも並び替えられるよう、▲ はヘッダーに置くべき。");
                Assert.IsNotNull(down, "折り畳んだままでも並び替えられるよう、▼ はヘッダーに置くべき。");
                Assert.AreEqual(expectedUp[i], up.enabledSelf, $"行 {i} の ▲ の有効状態（先頭のみ無効）。");
                Assert.AreEqual(expectedDown[i], down.enabledSelf, $"行 {i} の ▼ の有効状態（末尾のみ無効）。");
            }
        }

        [Test]
        public void Construct_DrawerProvidesHeaderSummary_SummaryPrecedesMoveButtons()
        {
            _so.WritableAdapterBindings.Add(new MockListViewSummaryBinding { Slug = "osc", _port = 9001 });
            ReloadSerializedObject();

            var view = new AdapterBindingsListView(_listProperty);

            var header = view.Q(className: Foldout.inputUssClassName);
            var summary = header.Q<Label>(className: AdapterBindingsListView.HeaderSummaryClassName);
            var up = header.Q<Button>(className: AdapterBindingsListView.HeaderMoveUpButtonClassName);
            var remove = header.Q<Button>(className: AdapterBindingsListView.HeaderRemoveButtonClassName);
            Assert.Less(header.IndexOf(summary), header.IndexOf(up), "要約はボタン群の手前に並べる。");
            Assert.Less(header.IndexOf(up), header.IndexOf(remove), "削除ボタンは右端に置く。");
        }
    }
}
