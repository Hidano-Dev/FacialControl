using System.Collections.Generic;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [TestFixture]
    [SmallTest]
    public class WiringSerializedMapperTests : SizedTestFixture
    {
        private TestFacialCharacterProfileSO _profile;
        private SerializedObject _serializedObject;
        private WiringSerializedMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            Undo.ClearAll();
            _profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                priority = 1,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable
                    {
                        id = "lipsync-overlay:a",
                        weight = 0.25f,
                        optionsJson = "{\"mode\":\"first\"}",
                    },
                    new InputSourceDeclarationSerializable
                    {
                        id = "input",
                        weight = 1f,
                        optionsJson = "{\"existing\":true}",
                    },
                },
                layerOverrideMask = new List<string> { "emotion" },
            });

            _serializedObject = new SerializedObject(_profile);
            _mapper = new WiringSerializedMapper();
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
        public void AddDeclaration_NewCanonicalId_AppendsIdAndWeightAndMarksDirty()
        {
            _mapper.AddDeclaration(_serializedObject, 0, "lipsync-overlay:i", 0.75f);

            Assert.That(_profile.Layers[0].inputSources, Has.Count.EqualTo(3));
            Assert.That(_profile.Layers[0].inputSources[2].id, Is.EqualTo("lipsync-overlay:i"));
            Assert.That(_profile.Layers[0].inputSources[2].weight, Is.EqualTo(0.75f));
            Assert.That(_profile.Layers[0].inputSources[2].optionsJson, Is.Empty);
            Assert.That(EditorUtility.IsDirty(_profile), Is.True);

            Undo.PerformUndo();

            Assert.That(_profile.Layers[0].inputSources, Has.Count.EqualTo(2));
        }

        [Test]
        public void AddDeclaration_DuplicateCanonicalId_ReplacesWithSingleEntryAndPreservesLastOptionsJson()
        {
            _profile.Layers[0].inputSources.Add(new InputSourceDeclarationSerializable
            {
                id = "input",
                weight = 0.4f,
                optionsJson = "{\"existing\":false,\"last\":true}",
            });

            _mapper.AddDeclaration(_serializedObject, 0, "input", 0.6f);

            Assert.That(_profile.Layers[0].inputSources, Has.Count.EqualTo(2));
            Assert.That(_profile.Layers[0].inputSources[1].id, Is.EqualTo("input"));
            Assert.That(_profile.Layers[0].inputSources[1].weight, Is.EqualTo(0.6f));
            Assert.That(_profile.Layers[0].inputSources[1].optionsJson, Is.EqualTo("{\"existing\":false,\"last\":true}"));
        }

        [Test]
        public void RemoveDeclaration_MatchingCanonicalId_RemovesAllDuplicatesInOneUndoStep()
        {
            _profile.Layers[0].inputSources.Add(new InputSourceDeclarationSerializable
            {
                id = "input",
                weight = 0.2f,
                optionsJson = "{}",
            });

            _mapper.RemoveDeclaration(_serializedObject, 0, "input");

            Assert.That(_profile.Layers[0].inputSources, Has.Count.EqualTo(1));
            Assert.That(_profile.Layers[0].inputSources[0].id, Is.EqualTo("lipsync-overlay:a"));

            Undo.PerformUndo();

            Assert.That(_profile.Layers[0].inputSources, Has.Count.EqualTo(3));
        }

        [Test]
        public void SetWeight_ExistingDeclaration_UpdatesWeightWithoutChangingOptionsJson()
        {
            _mapper.SetWeight(_serializedObject, 0, "input", 0.35f);

            Assert.That(_profile.Layers[0].inputSources[1].weight, Is.EqualTo(0.35f));
            Assert.That(_profile.Layers[0].inputSources[1].optionsJson, Is.EqualTo("{\"existing\":true}"));
        }

        [Test]
        public void SetLayerProperties_UpdatesSerializableLayerFields()
        {
            _mapper.SetLayerProperties(
                _serializedObject,
                0,
                "overlay-updated",
                4,
                ExclusionMode.LastWins,
                new[] { "emotion", "fx" });

            LayerDefinitionSerializable layer = _profile.Layers[0];
            Assert.That(layer.name, Is.EqualTo("overlay-updated"));
            Assert.That(layer.priority, Is.EqualTo(4));
            Assert.That(layer.exclusionMode, Is.EqualTo(ExclusionMode.LastWins));
            CollectionAssert.AreEqual(new[] { "emotion", "fx" }, layer.layerOverrideMask);
            Assert.That(EditorUtility.IsDirty(_profile), Is.True);

            Undo.PerformUndo();

            layer = _profile.Layers[0];
            Assert.That(layer.name, Is.EqualTo("overlay"));
            Assert.That(layer.priority, Is.EqualTo(1));
            Assert.That(layer.exclusionMode, Is.EqualTo(ExclusionMode.Blend));
            CollectionAssert.AreEqual(new[] { "emotion" }, layer.layerOverrideMask);
        }

        [Test]
        public void ContinuousWeightEdit_MultipleUpdates_CollapsesUndoToSingleStep()
        {
            _mapper.BeginContinuousWeight(_serializedObject, 0, "input");
            _mapper.SetWeightContinuous(_serializedObject, 0, "input", 0.2f);
            _mapper.SetWeightContinuous(_serializedObject, 0, "input", 0.8f);
            _mapper.EndContinuousWeight();

            Assert.That(_profile.Layers[0].inputSources[1].weight, Is.EqualTo(0.8f));

            Undo.PerformUndo();

            Assert.That(_profile.Layers[0].inputSources[1].weight, Is.EqualTo(1f));
        }
    }
}
