using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [TestFixture]
    [SmallTest]
    public class PhonemeSlotInitializerTests : SizedTestFixture
    {
        private FacialCharacterProfileSO _profile;

        [TearDown]
        public void TearDown()
        {
            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        [Test]
        public void EnsureReservedSlots_NoReservedSlotsDeclared_AppendsAllReservedSlots()
        {
            var initializer = new PhonemeSlotInitializer();
            _profile = CreateProfileWithSlots("custom");

            bool changed = initializer.EnsureReservedSlots(new SerializedObject(_profile));

            Assert.That(changed, Is.True);
            Assert.That(_profile.Slots, Is.EqualTo(new[]
            {
                "custom",
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
                PhonemeOverlaySlots.O,
            }));
        }

        [Test]
        public void EnsureReservedSlots_SomeReservedSlotsDeclared_AppendsOnlyMissingReservedSlots()
        {
            var initializer = new PhonemeSlotInitializer();
            _profile = CreateProfileWithSlots(PhonemeOverlaySlots.A, "custom", PhonemeOverlaySlots.O);

            bool changed = initializer.EnsureReservedSlots(new SerializedObject(_profile));

            Assert.That(changed, Is.True);
            Assert.That(_profile.Slots, Is.EqualTo(new[]
            {
                PhonemeOverlaySlots.A,
                "custom",
                PhonemeOverlaySlots.O,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
            }));
        }

        [Test]
        public void EnsureReservedSlots_AllReservedSlotsDeclared_ReturnsFalseAndPreservesSlots()
        {
            var initializer = new PhonemeSlotInitializer();
            _profile = CreateProfileWithSlots(
                "custom",
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
                PhonemeOverlaySlots.O);

            bool changed = initializer.EnsureReservedSlots(new SerializedObject(_profile));

            Assert.That(changed, Is.False);
            Assert.That(_profile.Slots, Is.EqualTo(new[]
            {
                "custom",
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.E,
                PhonemeOverlaySlots.O,
            }));
        }

        [Test]
        public void GetMissingReservedSlots_PartialDeclaration_ReturnsOnlyMissingReservedSlots()
        {
            var initializer = new PhonemeSlotInitializer();

            var missingSlots = initializer.GetMissingReservedSlots(new[]
            {
                "custom",
                PhonemeOverlaySlots.A,
                PhonemeOverlaySlots.E,
            });

            Assert.That(missingSlots, Is.EqualTo(new[]
            {
                PhonemeOverlaySlots.I,
                PhonemeOverlaySlots.U,
                PhonemeOverlaySlots.O,
            }));
        }

        private static FacialCharacterProfileSO CreateProfileWithSlots(params string[] slots)
        {
            var profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "Base",
                priority = 0,
            });

            var serializedObject = new SerializedObject(profile);
            serializedObject.Update();
            SerializedProperty slotsProperty = serializedObject.FindProperty("_slots");
            slotsProperty.ClearArray();

            for (int i = 0; i < slots.Length; i++)
            {
                slotsProperty.InsertArrayElementAtIndex(i);
                slotsProperty.GetArrayElementAtIndex(i).stringValue = slots[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }
    }
}
