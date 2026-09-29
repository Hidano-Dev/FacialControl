using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Adapters.AdapterBindings.InputSystem;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.InputSystem.Adapters.ScriptableObject;
using Hidano.FacialControl.InputSystem.Editor.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Hidano.FacialControl.Testing;
using InputBindingMode = Hidano.FacialControl.InputSystem.Adapters.ScriptableObject.BindingMode;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.InputSystem.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// <see cref="InputSystemAdapterBindingDrawer"/> の smoke テスト。
    /// 「CreatePropertyGUI が例外なく生成できる」ことと、実機で発生した
    /// 「FacialCharacterProfileSO 編集後に Inspector を別オブジェクトへ切り替えると、破棄済み
    /// SerializedObject を掴んだ定期リフレッシュ（overlaySlot 候補更新タイマー）が NullReferenceException を
    /// 大量発生させる」不具合の再発（m_NativeObjectPtr ガード＋タイマー停止）だけを守る。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class InputSystemAdapterBindingDrawerTests : SizedTestFixture
    {
        private const string BlinkSlotName = "blink";
        private const string WinkSlotName = "wink";

        private TestProfileSO _profileSo;
        private SerializedObject _serializedObject;

        [TearDown]
        public void TearDown()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;

            if (_profileSo != null)
            {
                Object.DestroyImmediate(_profileSo);
                _profileSo = null;
            }
        }

        // ====================================================================
        // smoke 1: 生成できる
        // ====================================================================

        [Test]
        public void CreatePropertyGUI_ProfileBinding_ReturnsRootWithoutThrowing()
        {
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            SetSlots(_profileSo, BlinkSlotName);
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            VisualElement root = null;
            Assert.DoesNotThrow(() => root = new InputSystemAdapterBindingDrawer().CreatePropertyGUI(bindingProperty));

            Assert.That(root, Is.Not.Null);
            Assert.That(root.Q<ListView>(InputSystemAdapterBindingDrawer.ExpressionBindingsListName), Is.Not.Null);
        }

        // ====================================================================
        // 実機不具合: Inspector 切替で破棄済み SerializedObject を掴む timer の NRE
        // ====================================================================

        [Test]
        public void TickOverlaySlotRefresh_SerializedObjectDisposed_ReturnsFalseWithoutError()
        {
            // 回帰テスト: FacialCharacterProfileSO 編集後に Inspector を別オブジェクトへ切り替えると、
            // 破棄済み SerializedObject に対して定期リフレッシュが so.Update() を呼び
            // NullReferenceException が大量発生していた。破棄後は false を返して停止すべき。
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            SetSlots(_profileSo, BlinkSlotName);
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);
            var dropdown = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.OverlaySlotDropdownName);
            var help = row.Q<HelpBox>(InputSystemAdapterBindingDrawer.OverlaySlotHelpName);

            _serializedObject.Dispose();

            bool result = true;
            Assert.DoesNotThrow(
                () => result = InvokeTickOverlaySlotRefresh(dropdown, help, bindingProperty, 0),
                "SerializedObject 破棄後のティックは例外を出さずに停止判定を返すべき。");
            Assert.That(result, Is.False, "破棄後のティックは false（タイマー停止）を返すべき。");
        }

        [Test]
        public void TickOverlaySlotRefresh_ValidSerializedObject_ReturnsTrueAndRefreshesChoices()
        {
            // 上記ガードが有効な SerializedObject まで止めてしまわないことの対照テスト。
            _profileSo = ScriptableObject.CreateInstance<TestProfileSO>();
            SetSlots(_profileSo, BlinkSlotName);
            _profileSo.WritableAdapterBindings.Add(CreateBinding(BlinkSlotName));
            SerializedProperty bindingProperty = CreateProfileBindingProperty(_profileSo);

            var row = new VisualElement();
            InvokeBindExpressionBindingRow(row, 0, bindingProperty);
            var dropdown = row.Q<DropdownField>(InputSystemAdapterBindingDrawer.OverlaySlotDropdownName);
            var help = row.Q<HelpBox>(InputSystemAdapterBindingDrawer.OverlaySlotHelpName);

            SetSlots(_profileSo, BlinkSlotName, WinkSlotName);
            bool result = InvokeTickOverlaySlotRefresh(dropdown, help, bindingProperty, 0);

            Assert.That(result, Is.True, "有効な SerializedObject に対するティックは true（継続）を返すべき。");
            Assert.That(dropdown.choices, Is.EqualTo(new[] { string.Empty, BlinkSlotName, WinkSlotName }));
        }

        // ====================================================================
        // ヘルパー
        // ====================================================================

        private SerializedProperty CreateProfileBindingProperty(TestProfileSO so)
        {
            _serializedObject?.Dispose();
            _serializedObject = new SerializedObject(so);
            _serializedObject.Update();
            SerializedProperty list = _serializedObject.FindProperty("_adapterBindings");
            Assert.That(list, Is.Not.Null);
            Assert.That(list.arraySize, Is.EqualTo(1));
            return list.GetArrayElementAtIndex(0);
        }

        private static InputSystemAdapterBinding CreateBinding(string overlaySlot)
        {
            var binding = new InputSystemAdapterBinding();
            binding.Configure(
                asset: null,
                actionMapName: "Expression",
                expressionBindings: new[]
                {
                    new ExpressionBindingEntry
                    {
                        bindingMode = InputBindingMode.Overlay,
                        actionName = "RightTrigger",
                        overlaySlot = overlaySlot,
                        overlayTargetLayer = "overlay",
                    },
                });
            return binding;
        }

        // 以下の private アクセスは、panel 未接続の EditMode で timer 本体を直接駆動するための準備。
        // assert は戻り値と DropdownField.choices（公開 API）で行う。
        private static void InvokeBindExpressionBindingRow(
            VisualElement row,
            int index,
            SerializedProperty bindingProperty)
        {
            MethodInfo method = typeof(InputSystemAdapterBindingDrawer).GetMethod(
                "BindExpressionBindingRow",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            method.Invoke(null, new object[] { row, index, bindingProperty });
        }

        private static bool InvokeTickOverlaySlotRefresh(
            DropdownField dropdown,
            HelpBox help,
            SerializedProperty bindingProperty,
            int index)
        {
            MethodInfo method = typeof(InputSystemAdapterBindingDrawer).GetMethod(
                "TickOverlaySlotRefresh",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null,
                "定期リフレッシュの本体は TickOverlaySlotRefresh として切り出されているべき。");
            try
            {
                return (bool)method.Invoke(null, new object[] { dropdown, help, bindingProperty, index });
            }
            catch (TargetInvocationException ex)
            {
                // 呼び出し先の例外をそのままテストへ伝搬させる。
                throw ex.InnerException ?? ex;
            }
        }

        private static void SetSlots(FacialCharacterProfileSO so, params string[] slots)
        {
            var serialized = new SerializedObject(so);
            serialized.Update();
            SerializedProperty slotsProperty = serialized.FindProperty("_slots");
            Assert.That(slotsProperty, Is.Not.Null);
            slotsProperty.ClearArray();
            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.InsertArrayElementAtIndex(i);
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            serialized.Dispose();
        }

        private sealed class TestProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
        }
    }
}
