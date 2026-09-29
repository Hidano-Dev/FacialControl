using System.Collections.Generic;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.RoutingEditor;
using Hidano.FacialControl.RoutingEditor.Graph;
using Hidano.FacialControl.Editor.Windows.Routing.Logic;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Hidano.FacialControl.Testing;
using Object = UnityEngine.Object;

namespace Hidano.FacialControl.RoutingEditor.Tests.EditMode
{
    /// <summary>
    /// <see cref="RoutingEditorWindow"/> と <see cref="RoutingGraphView"/> の smoke テスト。
    /// ノード/エッジの描画詳細は検証せず、次だけを守る。
    /// <list type="bullet">
    /// <item>生成できる: Open → CreateGUI で GraphView が構築され、無効なプロファイルでは開かない</item>
    /// <item>保存が通る: エッジ作成が <see cref="IWiringSerializedMapper"/> 経由で SO への書き込みに到達する</item>
    /// <item>破棄で例外を出さない</item>
    /// <item>無効 id（宣言はあるが解決できない入力）は孤立ノードとして表示するだけで SO を改変しない</item>
    /// </list>
    /// </summary>
    [TestFixture]
    [MediumTest] // RoutingEditorWindow（EditorWindow）の生成と Resources.FindObjectsOfTypeAll による後始末を伴うため Small 不可
    public class RoutingEditorWindowTests : SizedTestFixture
    {
        private FacialCharacterProfileSO _profile;
        private SerializedObject _serializedObject;
        private RecordingWiringSerializedMapper _mapper;

        [SetUp]
        public void SetUp()
        {
            _profile = ScriptableObject.CreateInstance<FacialCharacterProfileSO>();
            _profile.Layers.Add(new LayerDefinitionSerializable
            {
                name = "overlay",
                priority = 1,
                exclusionMode = ExclusionMode.Blend,
                inputSources = new List<InputSourceDeclarationSerializable>(),
            });
            _serializedObject = new SerializedObject(_profile);
            _mapper = new RecordingWiringSerializedMapper();
        }

        [TearDown]
        public void TearDown()
        {
            RoutingEditorWindow[] windows = Resources.FindObjectsOfTypeAll<RoutingEditorWindow>();
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] != null)
                {
                    Object.DestroyImmediate(windows[i]);
                }
            }

            _serializedObject?.Dispose();
            _serializedObject = null;

            if (_profile != null)
            {
                Object.DestroyImmediate(_profile);
                _profile = null;
            }
        }

        // ====================================================================
        // smoke 1: 生成できる
        // ====================================================================

        [Test]
        public void Open_NullProfile_ReturnsNullWithoutOpeningWindow()
        {
            LogAssert.Expect(LogType.Warning, new Regex("is null or invalid"));

            RoutingEditorWindow window = RoutingEditorWindow.Open(null);

            Assert.That(window, Is.Null);
            Assert.That(CountOpenWindows(), Is.EqualTo(0));
        }

        [Test]
        public void Open_ValidProfile_BuildsGraphViewWithLayerAndOutputNodes()
        {
            RoutingEditorWindow window = null;
            Assert.DoesNotThrow(() =>
            {
                window = RoutingEditorWindow.Open(_profile);
                window.CreateGUI();
            });

            Assert.That(window, Is.Not.Null);
            RoutingGraphView graphView = window.rootVisualElement.Q<RoutingGraphView>();
            Assert.That(graphView, Is.Not.Null);
            Assert.That(graphView.LayerNodeViews, Has.Count.EqualTo(1));
            Assert.That(graphView.OutputNodeView, Is.Not.Null);
        }

        [Test]
        public void Open_SameProfileTwice_ReturnsExistingWindowWithoutDuplicate()
        {
            RoutingEditorWindow first = RoutingEditorWindow.Open(_profile);
            first.CreateGUI();

            RoutingEditorWindow second = RoutingEditorWindow.Open(_profile);

            Assert.That(second, Is.SameAs(first));
            Assert.That(CountOpenWindows(), Is.EqualTo(1));
        }

        // ====================================================================
        // smoke 3: 破棄で例外を出さない
        // ====================================================================

        [Test]
        public void DestroyImmediate_AfterCreateGUI_DoesNotThrow()
        {
            RoutingEditorWindow window = RoutingEditorWindow.Open(_profile);
            window.CreateGUI();

            Assert.DoesNotThrow(() => Object.DestroyImmediate(window));
            Assert.That(CountOpenWindows(), Is.EqualTo(0));
        }

        // ====================================================================
        // smoke 2: 保存が通る（エッジ作成 → mapper 経由で SO へ）
        // ====================================================================

        [Test]
        public void GraphViewChanged_CreateEdge_AddsDeclarationThroughMapper()
        {
            RoutingGraphView graphView = CreateGraphView();
            var edge = new Edge
            {
                output = graphView.AdapterNodeViews[0].GetOutputPort("lipsync-overlay:a"),
                input = graphView.LayerNodeViews[0].InputPort,
            };

            GraphViewChange change = graphView.graphViewChanged(new GraphViewChange
            {
                edgesToCreate = new List<Edge> { edge },
            });

            Assert.That(_mapper.AddDeclarationCalls, Is.EqualTo(1));
            Assert.That(_mapper.LastLayerIndex, Is.EqualTo(0));
            Assert.That(_mapper.LastCanonicalId, Is.EqualTo("lipsync-overlay:a"));
            Assert.That(change.edgesToCreate, Has.Count.EqualTo(1));
            Assert.That(change.edgesToCreate[0], Is.InstanceOf<RoutingEdge>());
        }

        // ====================================================================
        // 無効 id 検出で SO を改変しない
        // ====================================================================

        [Test]
        public void SetInvalidInputs_UnknownId_RendersNonDeletableOrphanNodeWithoutWritingDeclarations()
        {
            RoutingGraphView graphView = CreateGraphView();

            graphView.SetInvalidInputs(new[] { new DanglingEdgeData(0, 2, "ulipsync:a") });

            Assert.That(graphView.OrphanInputNodes, Has.Count.EqualTo(1));
            OrphanInputNodeView orphanNode = graphView.OrphanInputNodes[0];
            Assert.That(orphanNode.Data.Id, Is.EqualTo("ulipsync:a"));
            // 孤立ノードは削除不可（UI 操作で宣言が消えない）。
            Assert.That(orphanNode.capabilities & Capabilities.Deletable, Is.EqualTo((Capabilities)0));
            // 表示のみで、SO への書き込み経路（mapper）は一切呼ばれない。
            Assert.That(_mapper.AddDeclarationCalls, Is.EqualTo(0));
            Assert.That(_mapper.RemoveDeclarationCalls, Is.EqualTo(0));
        }

        [Test]
        public void GraphViewChanged_RemoveCompositionEdge_KeepsLayerDeclarations()
        {
            // 合成エッジ（Layer→Composite Output）は構造的に常に存在するため削除不可。
            // 仮に elementsToRemove に渡されても、そのレイヤーの入力宣言を消してはならない。
            RoutingGraphView graphView = CreateGraphView();
            graphView.SetOutputNode(
                new OutputNodeData(new[]
                {
                    new OutputLayerData(0, "overlay", 1, ExclusionMode.Blend, new string[0]),
                }),
                _serializedObject,
                _mapper);
            graphView.SetCompositionEdges();
            Assert.That(graphView.CompositionEdges, Has.Count.EqualTo(1));
            Edge compositionEdge = graphView.CompositionEdges[0];
            Assert.That(compositionEdge.capabilities & Capabilities.Deletable, Is.EqualTo((Capabilities)0));

            graphView.graphViewChanged(new GraphViewChange
            {
                elementsToRemove = new List<GraphElement> { compositionEdge },
            });

            Assert.That(_mapper.RemoveDeclarationCalls, Is.EqualTo(0));
        }

        // ====================================================================
        // ヘルパー
        // ====================================================================

        private RoutingGraphView CreateGraphView()
        {
            var graphView = new RoutingGraphView();
            graphView.SetAdapterNodes(
                new[]
                {
                    new AdapterNodeData(
                        "ulipsync",
                        "ulipsync",
                        supportsAutoWire: true,
                        new[] { new AdapterOutputData("lipsync-overlay:a", "a") }),
                });
            graphView.SetLayerNodes(
                new[] { new LayerNodeData(0, "overlay", 1, ExclusionMode.Blend, new string[0]) },
                _serializedObject,
                _mapper);
            return graphView;
        }

        private static int CountOpenWindows()
        {
            return Resources.FindObjectsOfTypeAll<RoutingEditorWindow>().Length;
        }

        private sealed class RecordingWiringSerializedMapper : IWiringSerializedMapper
        {
            public int AddDeclarationCalls { get; private set; }

            public int RemoveDeclarationCalls { get; private set; }

            public int LastLayerIndex { get; private set; }

            public string LastCanonicalId { get; private set; } = string.Empty;

            public void AddDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
                AddDeclarationCalls++;
                LastLayerIndex = layerIndex;
                LastCanonicalId = canonicalId;
            }

            public void RemoveDeclaration(SerializedObject serializedObject, int layerIndex, string canonicalId)
            {
                RemoveDeclarationCalls++;
                LastLayerIndex = layerIndex;
                LastCanonicalId = canonicalId;
            }

            public void SetWeight(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
            }

            public void SetLayerProperties(
                SerializedObject serializedObject,
                int layerIndex,
                string layerName,
                int priority,
                ExclusionMode exclusionMode,
                IReadOnlyList<string> overrideMask)
            {
            }

            public void BeginContinuousWeight(SerializedObject serializedObject, int layerIndex, string canonicalId)
            {
            }

            public void SetWeightContinuous(SerializedObject serializedObject, int layerIndex, string canonicalId, float weight)
            {
            }

            public void EndContinuousWeight()
            {
            }
        }
    }
}
