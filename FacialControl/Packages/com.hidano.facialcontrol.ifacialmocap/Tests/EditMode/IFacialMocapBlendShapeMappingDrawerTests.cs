using Hidano.FacialControl.Adapters.AdapterBindings;
using Hidano.FacialControl.IFacialMocap.Editor.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.IFacialMocap.Tests.EditMode
{
    /// <summary>
    /// <see cref="IFacialMocapBlendShapeMappingDrawer"/> が調整欄を表示し、旧アセット（tuningVersion 0）を
    /// 既定値で見せ、編集時に調整値と tuningVersion を書き込むことを守る。
    /// </summary>
    [SmallTest]
    public class IFacialMocapBlendShapeMappingDrawerTests : SizedTestFixture
    {
        private IFacialMocapBlendShapeMappingDrawerTestAsset _asset;
        private SerializedObject _serializedObject;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<IFacialMocapBlendShapeMappingDrawerTestAsset>();
        }

        [TearDown]
        public void TearDown()
        {
            _serializedObject?.Dispose();
            _serializedObject = null;
            if (_asset != null)
            {
                Object.DestroyImmediate(_asset);
                _asset = null;
            }
        }

        private SerializedProperty Element(int index)
        {
            _serializedObject = new SerializedObject(_asset);
            return _serializedObject
                .FindProperty(nameof(IFacialMocapBlendShapeMappingDrawerTestAsset.Mappings))
                .GetArrayElementAtIndex(index);
        }

        [Test]
        public void CreatePropertyGUI_LegacyElement_ShowsToggleRangeAndWeightWithDefaults()
        {
            _asset.Mappings.Add(new IFacialMocapBlendShapeMapping { ifacialMocapName = "jawOpen", blendShapeName = "jawOpen" });

            VisualElement root = new IFacialMocapBlendShapeMappingDrawer().CreatePropertyGUI(Element(0));

            var toggle = root.Q<Toggle>(IFacialMocapBlendShapeMappingDrawer.EnabledToggleName);
            var slider = root.Q<MinMaxSlider>(IFacialMocapBlendShapeMappingDrawer.RangeSliderName);
            var weight = root.Q<FloatField>(IFacialMocapBlendShapeMappingDrawer.WeightFieldElementName);
            Assert.That(toggle, Is.Not.Null);
            Assert.That(slider, Is.Not.Null);
            Assert.That(weight, Is.Not.Null);
            Assert.That(toggle.value, Is.True);
            Assert.That(slider.value, Is.EqualTo(new Vector2(0f, 1f)));
            Assert.That(weight.value, Is.EqualTo(1f));
        }

        [Test]
        public void CreatePropertyGUI_TunedElement_ShowsStoredValues()
        {
            _asset.Mappings.Add(new IFacialMocapBlendShapeMapping("jawOpen", "jawOpen", false, 0.2f, 0.7f, 1.5f));

            VisualElement root = new IFacialMocapBlendShapeMappingDrawer().CreatePropertyGUI(Element(0));

            Assert.That(root.Q<Toggle>(IFacialMocapBlendShapeMappingDrawer.EnabledToggleName).value, Is.False);
            Assert.That(root.Q<MinMaxSlider>(IFacialMocapBlendShapeMappingDrawer.RangeSliderName).value,
                Is.EqualTo(new Vector2(0.2f, 0.7f)));
            Assert.That(root.Q<FloatField>(IFacialMocapBlendShapeMappingDrawer.WeightFieldElementName).value,
                Is.EqualTo(1.5f));
        }

        [Test]
        public void WriteTuning_LegacyElement_StoresValuesAndCurrentVersion()
        {
            _asset.Mappings.Add(new IFacialMocapBlendShapeMapping { ifacialMocapName = "jawOpen", blendShapeName = "jawOpen" });
            SerializedProperty element = Element(0);

            IFacialMocapBlendShapeMappingDrawer.WriteTuning(element, false, new Vector2(0.6f, 0.1f), 0.5f);
            _serializedObject.ApplyModifiedPropertiesWithoutUndo();

            IFacialMocapBlendShapeMapping stored = _asset.Mappings[0];
            Assert.That(stored.tuningVersion, Is.EqualTo(IFacialMocapBlendShapeMapping.CurrentTuningVersion));
            Assert.That(stored.enabled, Is.False);
            Assert.That(stored.rangeMin, Is.EqualTo(0.1f), "Min > Max は入れ替えて保存する。");
            Assert.That(stored.rangeMax, Is.EqualTo(0.6f));
            Assert.That(stored.weight, Is.EqualTo(0.5f));
            Assert.That(stored.ifacialMocapName, Is.EqualTo("jawOpen"), "名前欄は変更しない。");
        }
    }
}
