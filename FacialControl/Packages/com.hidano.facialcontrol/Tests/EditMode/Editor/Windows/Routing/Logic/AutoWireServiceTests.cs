#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System.Linq;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.LipSync.Adapters;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [TestFixture]
    [SmallTest]
    public class AutoWireServiceTests : SizedTestFixture
    {
        private FacialCharacterProfileSO _profile;

        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            Undo.ClearAll();
            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        [Test]
        public void AutoWire_OverlayLayer_WiresWeightOneAndDeclaresSlotsInSingleUndo()
        {
            _profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                inputSources =
                {
                    new InputSourceDeclarationSerializable
                    {
                        id = "lipsync-overlay:a",
                        weight = 0.25f,
                        optionsJson = "{\"keep\":true}",
                    },
                },
            });
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
            });

            var service = new AutoWireService();
            var binding = new ULipSyncAdapterBinding();
            var serializedObject = new SerializedObject(_profile);

            service.AutoWire(serializedObject, binding, _profile.Layers.Select(layer => layer.name).ToArray());

            CollectionAssert.AreEqual(
                new[]
                {
                    "lipsync-overlay:a",
                    "lipsync-overlay:i",
                    "lipsync-overlay:u",
                    "lipsync-overlay:e",
                    "lipsync-overlay:o",
                },
                _profile.Layers[0].inputSources.Select(source => source.id).ToArray());
            Assert.That(_profile.Layers[0].inputSources[0].weight, Is.EqualTo(0.25f));
            Assert.That(_profile.Layers[0].inputSources[0].optionsJson, Is.EqualTo("{\"keep\":true}"));
            Assert.That(_profile.Layers[0].inputSources.Skip(1).All(source => Mathf.Approximately(source.weight, 1f)), Is.True);
            CollectionAssert.AreEqual(
                new[] { "a", "i", "u", "e", "o" },
                _profile.Slots);

            Undo.PerformUndo();

            Assert.That(_profile.Layers[0].inputSources, Has.Count.EqualTo(1));
            Assert.That(_profile.Layers[0].inputSources[0].id, Is.EqualTo("lipsync-overlay:a"));
            Assert.That(_profile.Slots, Is.Empty);
        }

        [Test]
        public void AutoWire_NonDefaultLayerInputsBinding_DoesNothing()
        {
            _profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
            });

            var service = new AutoWireService();
            var binding = new LegacyOnlyBindingStub { Slug = "legacy-binding" };
            var serializedObject = new SerializedObject(_profile);

            service.AutoWire(serializedObject, binding, new[] { "overlay" });

            Assert.That(_profile.Layers[0].inputSources, Is.Empty);
            Assert.That(_profile.Slots, Is.Empty);
        }
    }
}
#endif
