using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.Osc.Editor.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Osc.Tests.EditMode.Adapters.AdapterBindings
{
    /// <summary>
    /// OSC Receiver drawer の対象レイヤー Dropdown が、実際の SerializedProperty（フィールド名）から
    /// 現在値を読むことを守る（フィールド名の変更で Dropdown が黙って壊れないこと）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public sealed class OscReceiverTargetLayerDrawerTests : SizedTestFixture
    {
        private OscHeaderSummaryTestHolder _holder;

        [TearDown]
        public void TearDown()
        {
            if (_holder != null)
            {
                Object.DestroyImmediate(_holder);
                _holder = null;
            }
        }

        [Test]
        public void CreatePropertyGUI_TargetLayerSet_ShowsCurrentValue()
        {
            DropdownField dropdown = CreateDropdown(new OscReceiverAdapterBinding { TargetLayer = "face" });

            Assert.AreEqual("face" + OscReceiverAdapterBindingDrawer.TargetLayerMissingSuffix, dropdown.value,
                "プロファイルに無いレイヤー名でも現在値を消さずに表示する。");
        }

        [Test]
        public void CreatePropertyGUI_TargetLayerUnset_ShowsUnspecified()
        {
            DropdownField dropdown = CreateDropdown(new OscReceiverAdapterBinding());

            Assert.AreEqual(OscReceiverAdapterBindingDrawer.TargetLayerUnspecifiedChoice, dropdown.value);
        }

        private DropdownField CreateDropdown(OscReceiverAdapterBinding binding)
        {
            _holder = ScriptableObject.CreateInstance<OscHeaderSummaryTestHolder>();
            _holder.Binding = binding;
            var serializedObject = new SerializedObject(_holder);
            SerializedProperty property = serializedObject.FindProperty(nameof(OscHeaderSummaryTestHolder.Binding));
            Assert.IsNotNull(property);

            VisualElement root = new OscReceiverAdapterBindingDrawer().CreatePropertyGUI(property);
            var dropdown = root.Q<DropdownField>(OscReceiverAdapterBindingDrawer.TargetLayerFieldElementName);
            Assert.IsNotNull(dropdown, "対象レイヤーの Dropdown が描画されること。");
            return dropdown;
        }
    }
}
