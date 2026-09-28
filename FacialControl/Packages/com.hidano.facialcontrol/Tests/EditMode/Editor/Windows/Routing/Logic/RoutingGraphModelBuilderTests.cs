#if FACIALCONTROL_HAS_LIPSYNC_MODULE
using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.LipSync.Adapters;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    [TestFixture]
    [SmallTest]
    public class RoutingGraphModelBuilderTests : SizedTestFixture
    {
        private TestFacialCharacterProfileSO _profile;
        private RoutingGraphModelBuilder _builder;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<TestFacialCharacterProfileSO>();
            _builder = new RoutingGraphModelBuilder();
        }

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
        public void Build_IsIdempotent()
        {
            _profile.WritableAdapterBindings.Add(new ULipSyncAdapterBinding());
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                priority = 2,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "lipsync-overlay:a", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "lipsync-overlay:i", weight = 0.5f },
                },
                layerOverrideMask = new List<string> { "emotion" },
            });
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "emotion",
                priority = 0,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "ulipsync", weight = 0.25f },
                    new InputSourceDeclarationSerializable { id = "unknown:ghost", weight = 0.75f },
                },
            });

            RoutingGraphModel first = _builder.Build(_profile);
            RoutingGraphModel second = _builder.Build(_profile);

            CollectionAssert.AreEqual(
                first.AdapterNodes.SelectMany(node => node.Outputs).Select(output => output.CanonicalId).ToArray(),
                second.AdapterNodes.SelectMany(node => node.Outputs).Select(output => output.CanonicalId).ToArray());
            CollectionAssert.AreEqual(
                first.LayerNodes.Select(node => node.Name).ToArray(),
                second.LayerNodes.Select(node => node.Name).ToArray());
            CollectionAssert.AreEqual(
                first.OutputNode.OrderedLayers.Select(layer => layer.Name).ToArray(),
                second.OutputNode.OrderedLayers.Select(layer => layer.Name).ToArray());
            CollectionAssert.AreEqual(
                first.Edges.Select(edge => $"{edge.LayerIndex}:{edge.CanonicalId}:{edge.Weight}").ToArray(),
                second.Edges.Select(edge => $"{edge.LayerIndex}:{edge.CanonicalId}:{edge.Weight}").ToArray());
            CollectionAssert.AreEqual(
                first.InvalidEdges.Select(edge => $"{edge.LayerIndex}:{edge.DeclarationIndex}:{edge.Id}").ToArray(),
                second.InvalidEdges.Select(edge => $"{edge.LayerIndex}:{edge.DeclarationIndex}:{edge.Id}").ToArray());

            CollectionAssert.AreEqual(
                new[]
                {
                    "lipsync-overlay:a",
                    "lipsync-overlay:i",
                    "lipsync-overlay:u",
                    "lipsync-overlay:e",
                    "lipsync-overlay:o",
                    "ulipsync",
                },
                first.AdapterNodes.SelectMany(node => node.Outputs).Select(output => output.CanonicalId).ToArray());
            CollectionAssert.AreEqual(
                new[] { "emotion", "overlay" },
                first.OutputNode.OrderedLayers.Select(layer => layer.Name).ToArray());

            // 1 binding = 1 アダプターノードへグルーピングされる。
            Assert.That(first.AdapterNodes.Count, Is.EqualTo(1));
            Assert.That(first.AdapterNodes[0].BindingSlug, Is.EqualTo("ulipsync"));
            Assert.That(first.AdapterNodes[0].DisplayName, Is.EqualTo("ulipsync"));
            Assert.That(first.AdapterNodes[0].SupportsAutoWire, Is.True);

            // レイヤーの接続入力源（ウェイト一覧用）が宣言順で詰まる。
            CollectionAssert.AreEqual(
                new[] { "lipsync-overlay:a", "lipsync-overlay:i" },
                first.LayerNodes[0].Inputs.Select(input => input.CanonicalId).ToArray());
            CollectionAssert.AreEqual(
                new[] { 1f, 0.5f },
                first.LayerNodes[0].Inputs.Select(input => input.Weight).ToArray());
            CollectionAssert.AreEqual(
                new[] { "ulipsync" },
                first.LayerNodes[1].Inputs.Select(input => input.CanonicalId).ToArray());

            // OutputNode の各行に ExclusionMode が転記される（priority 昇順: emotion=0, overlay=2）。
            CollectionAssert.AreEqual(
                new[] { ExclusionMode.LastWins, ExclusionMode.Blend },
                first.OutputNode.OrderedLayers.Select(layer => layer.ExclusionMode).ToArray());
        }

        [Test]
        public void Build_AllDeclarations_ClassifiedAsNormalOrInvalid()
        {
            _profile.WritableAdapterBindings.Add(new ULipSyncAdapterBinding());
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                priority = 1,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "lipsync-overlay:a", weight = 1f },
                    new InputSourceDeclarationSerializable { id = "ulipsync:a", weight = 1f },
                },
            });
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "lipsync",
                priority = 3,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>
                {
                    new InputSourceDeclarationSerializable { id = "ulipsync", weight = 0.5f },
                    new InputSourceDeclarationSerializable { id = "unknown:ghost", weight = 0.2f },
                },
                layerOverrideMask = new List<string> { "overlay" },
            });

            RoutingGraphModel model = _builder.Build(_profile);

            int declarationCount = _profile.Layers.Sum(layer => layer.inputSources.Count);
            string[] allDeclarationCoordinates = _profile.Layers
                .SelectMany(
                    (layer, layerIndex) => layer.inputSources.Select(
                        (_, declarationIndex) => $"{layerIndex}:{declarationIndex}"))
                .ToArray();
            string[] classifiedCoordinates = model.Edges
                .Select(edge => $"{edge.LayerIndex}:{FindDeclarationIndex(_profile.Layers[edge.LayerIndex], edge.CanonicalId)}")
                .Concat(model.InvalidEdges.Select(edge => $"{edge.LayerIndex}:{edge.DeclarationIndex}"))
                .ToArray();

            Assert.That(model.Edges.Count + model.InvalidEdges.Count, Is.EqualTo(declarationCount));
            CollectionAssert.AreEquivalent(allDeclarationCoordinates, classifiedCoordinates);
            Assert.That(classifiedCoordinates.Distinct().Count(), Is.EqualTo(declarationCount));

            CollectionAssert.AreEqual(
                new[]
                {
                    "0:lipsync-overlay:a:1",
                    "1:ulipsync:0.5",
                },
                model.Edges.Select(edge => $"{edge.LayerIndex}:{edge.CanonicalId}:{edge.Weight:g}").ToArray());
            CollectionAssert.AreEqual(
                new[]
                {
                    "0:1:ulipsync:a",
                    "1:1:unknown:ghost",
                },
                model.InvalidEdges.Select(edge => $"{edge.LayerIndex}:{edge.DeclarationIndex}:{edge.Id}").ToArray());
            CollectionAssert.AreEqual(
                new[] { "overlay" },
                model.LayerNodes[1].OverrideMask.ToArray());
        }

        private static int FindDeclarationIndex(LayerDefinitionSerializable layer, string canonicalId)
        {
            for (int declarationIndex = 0; declarationIndex < layer.inputSources.Count; declarationIndex++)
            {
                if (layer.inputSources[declarationIndex].id == canonicalId)
                {
                    return declarationIndex;
                }
            }

            Assert.Fail($"Declaration '{canonicalId}' was not found in the source profile.");
            return -1;
        }
    }
}
#endif
