using System.Collections.Generic;
using System.Linq;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using Hidano.FacialControl.Tests.EditMode.Adapters.ScriptableObjectTests.AdapterBindings;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Windows.Routing.Logic
{
    /// <summary>
    /// 対象レイヤーを指定した binding（<see cref="IAdapterBindingTargetLayerInput"/>）の入力源が、
    /// ランタイムと同じ規則でルーティングエディタ上の配線として見える（手動宣言があれば手動だけ）ことを守る。
    /// </summary>
    [TestFixture]
    [SmallTest]
    public class RoutingGraphModelBuilderTargetLayerTests : SizedTestFixture
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
        public void Build_TargetLayerWithoutManualDeclaration_AddsAutoEdgeToTargetLayer()
        {
            AddLayer("emotion");
            AddLayer("lipsync", ("mic", 1f));
            _profile.WritableAdapterBindings.Add(new DeclaredOnlyBinding("mic"));
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", "lipsync"));

            RoutingGraphModel model = _builder.Build(_profile);

            WiringEdgeData[] oscEdges = model.Edges.Where(edge => edge.CanonicalId == "osc").ToArray();
            Assert.AreEqual(1, oscEdges.Length);
            Assert.AreEqual(1, oscEdges[0].LayerIndex, "対象レイヤーに繋がって見える。");
            Assert.IsTrue(oscEdges[0].IsAutoDeclared, "手動宣言と区別する。");
            Assert.AreEqual(1f, oscEdges[0].Weight);
            CollectionAssert.IsEmpty(model.InvalidEdges, "自動宣言の id を未解決として扱わない。");

            LayerInputData[] inputs = model.LayerNodes[1].Inputs.ToArray();
            CollectionAssert.AreEqual(new[] { "mic", "osc" }, inputs.Select(input => input.CanonicalId).ToArray());
            CollectionAssert.AreEqual(new[] { false, true }, inputs.Select(input => input.IsAutoDeclared).ToArray());
            CollectionAssert.IsEmpty(model.LayerNodes[0].Inputs);
        }

        [Test]
        public void Build_TargetLayerUnspecified_AddsAutoEdgeToFirstLayer()
        {
            AddLayer("emotion");
            AddLayer("eye");
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", null));

            RoutingGraphModel model = _builder.Build(_profile);

            WiringEdgeData edge = model.Edges.Single();
            Assert.AreEqual(0, edge.LayerIndex);
            Assert.IsTrue(edge.IsAutoDeclared);
        }

        [Test]
        public void Build_ManualDeclarationExists_ShowsManualEdgeOnly()
        {
            AddLayer("emotion", ("osc", 0.5f));
            AddLayer("lipsync");
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", "lipsync"));

            RoutingGraphModel model = _builder.Build(_profile);

            WiringEdgeData edge = model.Edges.Single();
            Assert.AreEqual(0, edge.LayerIndex, "ランタイムと同じく手動宣言を優先する。");
            Assert.IsFalse(edge.IsAutoDeclared);
            Assert.AreEqual(0.5f, edge.Weight);
            CollectionAssert.IsEmpty(model.LayerNodes[1].Inputs);
        }

        [Test]
        public void Build_TargetLayerMissing_AddsNoAutoEdge()
        {
            AddLayer("emotion");
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", "face"));

            RoutingGraphModel model = _builder.Build(_profile);

            CollectionAssert.IsEmpty(model.Edges);
            CollectionAssert.IsEmpty(model.InvalidEdges);
        }

        [Test]
        public void Build_BindingDisabled_AddsNoAutoEdge()
        {
            AddLayer("emotion");
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", "emotion") { Disabled = true });

            RoutingGraphModel model = _builder.Build(_profile);

            CollectionAssert.IsEmpty(model.Edges);
        }

        [Test]
        public void Build_FirstLayerUnnamed_AddsAutoEdgeToFirstNamedLayer()
        {
            AddLayer(" ");
            AddLayer("emotion");
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", null));

            RoutingGraphModel model = _builder.Build(_profile);

            Assert.AreEqual(1, model.Edges.Single().LayerIndex, "ランタイムは名前の無いレイヤーを読み捨てるので、先頭は 'emotion'。");
        }

        [Test]
        public void Build_ManualDeclarationOnlyOnDroppedDuplicateLayer_StillAddsAutoEdge()
        {
            AddLayer("emotion");
            AddLayer("emotion", ("osc", 0.5f));
            _profile.WritableAdapterBindings.Add(new TargetLayerBinding("osc", "emotion"));

            RoutingGraphModel model = _builder.Build(_profile);

            WiringEdgeData autoEdge = model.Edges.Single(edge => edge.IsAutoDeclared);
            Assert.AreEqual(0, autoEdge.LayerIndex, "ランタイムは同名の 2 件目のレイヤーと宣言を読み捨てるので、自動で補う。");
            Assert.AreEqual("osc", autoEdge.CanonicalId);
        }

        [Test]
        public void Build_AutoIdWithoutSourcePort_AddsNoAutoEdge()
        {
            AddLayer("emotion");
            _profile.WritableAdapterBindings.Add(new TargetLayerOnlyBinding("osc", "emotion"));

            RoutingGraphModel model = _builder.Build(_profile);

            CollectionAssert.IsEmpty(model.Edges, "ソースポートの無い id は描けないので足さない。");
        }

        private void AddLayer(string name, params (string id, float weight)[] declarations)
        {
            var inputSources = new List<InputSourceDeclarationSerializable>();
            for (int i = 0; i < declarations.Length; i++)
            {
                inputSources.Add(new InputSourceDeclarationSerializable
                {
                    id = declarations[i].id,
                    weight = declarations[i].weight,
                });
            }

            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = name,
                priority = _profile.Layers.Count,
                exclusionMode = ExclusionMode.LastWins,
                inputSources = inputSources,
            });
        }

        private class TargetLayerOnlyBinding : AdapterBindingBase, IAdapterBindingTargetLayerInput
        {
            private readonly string _targetLayer;

            public TargetLayerOnlyBinding(string slug, string targetLayer)
            {
                Slug = slug;
                _targetLayer = targetLayer;
            }

            public string TargetLayerName => _targetLayer;

            // 起動していない binding を表す（Editor は設定上の id を使う）。
            public string TargetLayerInputSourceId => null;

            public string ConfiguredTargetLayerInputSourceId => Slug;
        }

        private sealed class DeclaredOnlyBinding : AdapterBindingBase, IAdapterBindingDeclaredInputs
        {
            public DeclaredOnlyBinding(string slug)
            {
                Slug = slug;
            }

            public IEnumerable<string> GetDeclaredInputSourceIds()
            {
                yield return Slug;
            }
        }

        private sealed class TargetLayerBinding : TargetLayerOnlyBinding, IAdapterBindingDeclaredInputs
        {
            public TargetLayerBinding(string slug, string targetLayer)
                : base(slug, targetLayer)
            {
            }

            public IEnumerable<string> GetDeclaredInputSourceIds()
            {
                yield return Slug;
            }
        }
    }
}
