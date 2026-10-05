using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Editor.Inspector.AdapterBindings;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.AdapterBindings;
using Hidano.FacialControl.Timeline.Editor.Inspector;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// Timeline binding の PropertyDrawer（Slug + 有効トグルのみ、legacy 残存時の HelpBox、ヘッダー要約）の生成・破棄 smoke と、
    /// legacy あり / なしの HelpBox 表示を固定する。SerializedObject と Undo を使うため Medium。
    /// </summary>
    [MediumTest]
    public sealed class TimelineAdapterBindingDrawerTests : SizedTestFixture
    {
        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        [Test]
        public void CreatePropertyGUI_ShowsSlugAndEnabledOnly()
        {
            SerializedProperty property = CreateBindingProperty(out _, legacy: false);

            VisualElement root = new TimelineAdapterBindingDrawer().CreatePropertyGUI(property);

            Assert.That(root.Q<AdapterBindingSlugField>(), Is.Not.Null);
            Assert.That(root.Q<Toggle>(TimelineAdapterBindingDrawer.EnabledFieldElementName), Is.Not.Null);
            Assert.That(root.Query<PropertyField>().ToList(), Is.Empty, "legacy フィールドを PropertyField で出さない");
        }

        [Test]
        public void CreatePropertyGUI_NoLegacyFields_HidesHelpBox()
        {
            SerializedProperty property = CreateBindingProperty(out _, legacy: false);

            VisualElement root = new TimelineAdapterBindingDrawer().CreatePropertyGUI(property);

            VisualElement legacy = root.Q(TimelineAdapterBindingDrawer.LegacyContainerName);
            Assert.That(legacy, Is.Not.Null);
            Assert.That(legacy.style.display.value, Is.EqualTo(DisplayStyle.None));
        }

        [Test]
        public void CreatePropertyGUI_LegacyFields_ShowsHelpBoxAndClearButton()
        {
            SerializedProperty property = CreateBindingProperty(out _, legacy: true);

            VisualElement root = new TimelineAdapterBindingDrawer().CreatePropertyGUI(property);

            VisualElement legacy = root.Q(TimelineAdapterBindingDrawer.LegacyContainerName);
            Assert.That(legacy.style.display.value, Is.EqualTo(DisplayStyle.Flex));
            HelpBox helpBox = legacy.Q<HelpBox>();
            Assert.That(helpBox, Is.Not.Null);
            Assert.That(helpBox.text, Does.Contain("旧フィールドは再生に使われません"));
            Assert.That(legacy.Q<Button>(TimelineAdapterBindingDrawer.ClearLegacyButtonName), Is.Not.Null);
        }

        [Test]
        public void ClearLegacyFields_RemovesLegacyAndUndoRestores()
        {
            SerializedProperty property = CreateBindingProperty(out _, legacy: true);
            Undo.IncrementCurrentGroup();

            bool cleared = TimelineAdapterBindingDrawer.ClearLegacyFields(property);

            Assert.That(cleared, Is.True);
            Assert.That(CurrentBinding(property).HasLegacyFields, Is.False);
            Assert.That(TimelineAdapterBindingDrawer.HasLegacyFields(property), Is.False);

            Undo.PerformUndo();
            property.serializedObject.Update();

            Assert.That(CurrentBinding(property).HasLegacyFields, Is.True, "Undo で旧フィールドが戻る");
        }

        [Test]
        public void GetHeaderSummary_ReflectsEnabledFlag()
        {
            SerializedProperty property = CreateBindingProperty(out TimelineAdapterBinding binding, legacy: false);
            var drawer = new TimelineAdapterBindingDrawer();

            Assert.That(drawer.GetHeaderSummary(property).Text, Is.EqualTo("Timeline / 有効"));

            binding.Enabled = false;
            property.serializedObject.Update();

            Assert.That(drawer.GetHeaderSummary(property).Text, Is.EqualTo("Timeline / 無効"));
        }

        [Test]
        public void CreatePropertyGUI_ThenDestroyTarget_DoesNotThrow()
        {
            SerializedProperty property = CreateBindingProperty(out _, legacy: true);
            VisualElement root = new TimelineAdapterBindingDrawer().CreatePropertyGUI(property);
            Assert.That(root, Is.Not.Null);

            Assert.DoesNotThrow(() =>
            {
                for (int i = 0; i < _created.Count; i++)
                {
                    Object.DestroyImmediate(_created[i]);
                }
            });
        }

        private SerializedProperty CreateBindingProperty(out TimelineAdapterBinding binding, bool legacy)
        {
            var so = ScriptableObject.CreateInstance<TimelineTestProfileSO>();
            _created.Add(so);
            binding = new TimelineAdapterBinding();
            if (legacy)
            {
                var names = (List<string>)typeof(TimelineAdapterBinding)
                    .GetField("targetLayerNames", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(binding);
                names.Add("emotion");
            }

            so.WritableAdapterBindings.Add(binding);
            var serialized = new SerializedObject(so);
            return serialized.FindProperty("_adapterBindings").GetArrayElementAtIndex(0);
        }

        private static TimelineAdapterBinding CurrentBinding(SerializedProperty property)
        {
            var so = (TimelineTestProfileSO)property.serializedObject.targetObject;
            return (TimelineAdapterBinding)so.WritableAdapterBindings[0];
        }
    }
}
