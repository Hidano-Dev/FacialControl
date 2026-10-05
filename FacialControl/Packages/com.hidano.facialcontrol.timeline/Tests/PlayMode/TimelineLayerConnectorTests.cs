using System;
using System.Collections;
using System.Collections.Generic;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Adapters.Assets;
using Hidano.FacialControl.Timeline.Adapters.InputSources;
using Hidano.FacialControl.Timeline.Adapters.Session;
using Hidano.FacialControl.Timeline.Domain.Diagnostics;
using Hidano.FacialControl.Timeline.Domain.Models;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Timeline.Tests.PlayMode
{
    /// <summary>
    /// <see cref="TimelineLayerConnector"/> の接続 / 解放と、旧 <c>:state</c> 宣言の検出を、
    /// 実際に初期化した <see cref="FacialController"/> で検証する。
    /// </summary>
    /// <remarks>
    /// FacialController の初期化は app LifetimeScope（PlayMode でのみ生成される）を親に child scope を build し、
    /// registry・観測バス・overlay suppress の active provider をそこで用意する。EditMode では registry が null のまま
    /// になり宣言経路や active provider を再現できないため、core の <c>FacialControllerTests</c> と同じく PlayMode に置く。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public sealed class TimelineLayerConnectorTests : SizedTestFixture
    {
        private const string EmotionLayer = "emotion";
        private const string ValueId = "timeline:emotion";
        private const string StateId = "timeline:emotion:state";
        private const string NonAsciiLayer = "感情";
        private static readonly string[] BlendShapeNames = { "smile", "frown" };

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();
        private AdapterSlug _slug;

        [SetUp]
        public void SetUp()
        {
            FacialControllerRendererOwnership.Clear();
            _slug = AdapterSlug.Parse("timeline");
        }

        [TearDown]
        public void TearDown()
        {
            FacialControllerRendererOwnership.Clear();
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_created[i]);
                }
            }

            _created.Clear();
        }

        // ================================================================
        // 接続
        // ================================================================

        [UnityTest]
        public IEnumerator Connect_MatchedLayer_ValueSinkReachesBlendShape()
        {
            FacialProfile profile = CreateProfile(EmotionLayer);
            Host host = CreateHost(profile);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);
            var diagnostics = new FacialTimelineDiagnostics();

            ConnectOutcome outcome = connector.Connect(
                Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), diagnostics);

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.Connected));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnected, EmotionLayer), Is.True);
            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.True);
            Assert.That(connector.ConnectedLayerNames, Is.EqualTo(new[] { EmotionLayer }));
            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);
            Assert.That(valueSink.TryGetBufferIndex("smile", out int bufferIndex), Is.True);

            valueSink.SetValue(bufferIndex, 0.6f);
            yield return null;

            Assert.That(host.Renderer.GetBlendShapeWeight(0), Is.EqualTo(60f).Within(0.01f),
                "値 sink の値が weight 1 で Aggregate を経て BlendShape へ届くこと");
            Assert.That(host.Renderer.GetBlendShapeWeight(1), Is.EqualTo(0f).Within(0.01f));
        }

        [Test]
        public void Connect_MatchedLayer_StateSinkIsNotInRegistryNorBoundToLayer()
        {
            FacialProfile profile = CreateProfile(EmotionLayer);
            Host host = CreateHost(profile);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);

            ConnectOutcome outcome = connector.Connect(
                Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), new FacialTimelineDiagnostics());

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.Connected));
            Assert.That(connector.TryGetStateSink(EmotionLayer, out TimelineExpressionStateSink stateSink), Is.True);
            Assert.That(stateSink, Is.Not.Null);
            Assert.That(host.Controller.InputSourceRegistry.TryResolve(StateId, out _), Is.False,
                "state sink は registry に登録しない（宣言経路でレイヤーに繋がる口を物理的に無くす）");
            Assert.That(host.Controller.InputSourceRegistry.TryResolve(ValueId, out IInputSource registered), Is.True);
            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);
            Assert.That(registered, Is.SameAs(valueSink), "値 sink は registry に登録する");
            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, StateId), Is.False,
                "Connected の状態で state sink がレイヤー入力源に接続されていないこと");
        }

        [Test]
        public void Connect_StateSink_IsObservedByOverlaySuppressActiveProvider()
        {
            FacialProfile profile = CreateProfile(EmotionLayer);
            Host host = CreateHost(profile);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);
            connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), new FacialTimelineDiagnostics());
            Assert.That(connector.TryGetStateSink(EmotionLayer, out TimelineExpressionStateSink stateSink), Is.True);
            IActiveExpressionProvider provider = host.Probe.ActiveExpressionProvider;
            Assert.That(provider, Is.Not.Null, "fixture: binding が active provider を受け取っていること");

            stateSink.TriggerOn("smile");

            Expression? top = provider.TryGetTopActiveExpression(EmotionLayer);
            Assert.That(top.HasValue, Is.True, "state sink の active 表情が overlay suppress の active provider に見えること");
            Assert.That(top.Value.Id, Is.EqualTo("smile"));

            connector.Disconnect();

            Assert.That(provider.TryGetTopActiveExpression(EmotionLayer).HasValue, Is.False,
                "解放後は active provider から外れること");
        }

        [Test]
        public void Connect_DeclaredValueSink_SkipsOwnBindAndRecordsInfo()
        {
            FacialProfile profile = CreateProfile(
                EmotionLayer,
                new[] { new InputSourceDeclaration(ValueId, 0.5f, null) });
            Host host = CreateHost(profile);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);
            var diagnostics = new FacialTimelineDiagnostics();

            ConnectOutcome outcome = connector.Connect(
                Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), diagnostics);

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.Connected));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnectionSkippedDeclared, EmotionLayer), Is.True);
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerConnected), Is.False);
            Assert.That(Severity(diagnostics, TimelineDiagnosticCode.LayerConnectionSkippedDeclared),
                Is.EqualTo(TimelineDiagnosticSeverity.Info));
            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.True,
                "宣言経路で後付け接続されていること");

            connector.Disconnect();

            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False,
                "registry からの登録解除で宣言経路のスロットも外れること");
            Assert.That(host.Controller.InputSourceRegistry.TryResolve(ValueId, out _), Is.False);
        }

        [UnityTest]
        public IEnumerator Connect_DeclaredValueSink_UsesDeclaredWeight()
        {
            FacialProfile profile = CreateProfile(
                EmotionLayer,
                new[] { new InputSourceDeclaration(ValueId, 0.5f, null) });
            Host host = CreateHost(profile);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);
            connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), new FacialTimelineDiagnostics());
            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);
            Assert.That(valueSink.TryGetBufferIndex("smile", out int bufferIndex), Is.True);

            valueSink.SetValue(bufferIndex, 0.6f);
            yield return null;

            Assert.That(host.Renderer.GetBlendShapeWeight(0), Is.EqualTo(30f).Within(0.01f),
                "宣言 weight 0.5 が優先されること（0.6 * 0.5）");
        }

        [Test]
        public void Connect_NonAsciiLayer_UsesIndexFallbackIdAndRecordsInfo()
        {
            FacialProfile profile = CreateProfile(NonAsciiLayer);
            Host host = CreateHost(profile);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);
            var diagnostics = new FacialTimelineDiagnostics();

            ConnectOutcome outcome = connector.Connect(
                Derive(NonAsciiLayer, 0), profile, BlendShapeNames, CreateBake(NonAsciiLayer), diagnostics);

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.Connected));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LayerSinkIdFallback, NonAsciiLayer), Is.True);
            Assert.That(Severity(diagnostics, TimelineDiagnosticCode.LayerSinkIdFallback),
                Is.EqualTo(TimelineDiagnosticSeverity.Info));
            Assert.That(host.Controller.IsLayerInputSourceBound(NonAsciiLayer, "timeline:layer0"), Is.True);
            Assert.That(host.Controller.InputSourceRegistry.TryResolve("timeline:layer0", out _), Is.True);
        }

        // ================================================================
        // 旧 :state 宣言の検出
        // ================================================================

        [Test]
        public void Connect_LegacyStateDeclaration_AbortsWithoutRegisteringAnything()
        {
            FacialProfile profile = CreateProfile(
                EmotionLayer,
                new[] { new InputSourceDeclaration(StateId, 1f, null) });
            Host host = CreateHost(profile);
            IInputSourceRegistry registry = host.Controller.InputSourceRegistry;
            string[] idsBefore = Snapshot(registry.RegisteredIds);
            var connector = new TimelineLayerConnector(host.Controller, registry, _slug);
            var diagnostics = new FacialTimelineDiagnostics();

            ConnectOutcome outcome = connector.Connect(
                Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), diagnostics);

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.LegacyStateDeclaration));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.True);
            Assert.That(Severity(diagnostics, TimelineDiagnosticCode.LegacyStateDeclaration),
                Is.EqualTo(TimelineDiagnosticSeverity.Error));
            Assert.That(SubjectOf(diagnostics, TimelineDiagnosticCode.LegacyStateDeclaration),
                Does.Contain(EmotionLayer).And.Contain(StateId), "件名はレイヤー名 + 宣言 id");
            Assert.That(Snapshot(registry.RegisteredIds), Is.EqualTo(idsBefore), "registry は不変");
            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False, "レイヤー構成は不変");
            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, StateId), Is.False);
            Assert.That(connector.ConnectedLayerNames, Is.Empty);
            Assert.That(connector.TryGetValueSink(EmotionLayer, out _), Is.False);
        }

        [Test]
        public void Connect_IndexFormLegacyStateDeclaration_IsDetected()
        {
            FacialProfile profile = CreateProfile(
                NonAsciiLayer,
                new[] { new InputSourceDeclaration("timeline:layer0:state", 1f, null) });
            Host host = CreateHost(profile);
            IInputSourceRegistry registry = host.Controller.InputSourceRegistry;
            string[] idsBefore = Snapshot(registry.RegisteredIds);
            var connector = new TimelineLayerConnector(host.Controller, registry, _slug);
            var diagnostics = new FacialTimelineDiagnostics();

            ConnectOutcome outcome = connector.Connect(
                Derive(NonAsciiLayer, 0), profile, BlendShapeNames, CreateBake(NonAsciiLayer), diagnostics);

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.LegacyStateDeclaration));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.True);
            Assert.That(Snapshot(registry.RegisteredIds), Is.EqualTo(idsBefore));
            Assert.That(host.Controller.IsLayerInputSourceBound(NonAsciiLayer, "timeline:layer0"), Is.False);
        }

        [Test]
        public void Connect_StateIdAlreadyBoundToLayer_RollsBackAndAborts()
        {
            // 静的走査をすり抜けた経路（宣言以外）で state id がレイヤーに繋がっている場合の動的検出。
            FacialProfile profile = CreateProfile(EmotionLayer);
            Host host = CreateHost(profile);
            IInputSourceRegistry registry = host.Controller.InputSourceRegistry;
            var stray = new StrayValueSource(StateId, BlendShapeNames.Length);
            Assert.That(host.Controller.TryBindLayerInputSource(EmotionLayer, StateId, stray, 1f), Is.True);
            string[] idsBefore = Snapshot(registry.RegisteredIds);
            var connector = new TimelineLayerConnector(host.Controller, registry, _slug);
            var diagnostics = new FacialTimelineDiagnostics();

            ConnectOutcome outcome = connector.Connect(
                Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), diagnostics);

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.LegacyStateDeclaration));
            Assert.That(diagnostics.Contains(TimelineDiagnosticCode.LegacyStateDeclaration), Is.True);
            Assert.That(Snapshot(registry.RegisteredIds), Is.EqualTo(idsBefore), "登録済みの値 sink は戻されること");
            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False,
                "自前で接続した値 sink は戻されること");
            Assert.That(connector.ConnectedLayerNames, Is.Empty);
        }

        [Test]
        public void Connect_ControllerNotInitialized_ReturnsControllerNotInitialized()
        {
            FacialProfile profile = CreateProfile(EmotionLayer);
            var root = new GameObject("ConnectorHostUninitialized");
            _created.Add(root);
            root.AddComponent<Animator>();
            var controller = root.AddComponent<FacialController>();
            var connector = new TimelineLayerConnector(controller, new InputSourceRegistry(), _slug);

            ConnectOutcome outcome = connector.Connect(
                Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), new FacialTimelineDiagnostics());

            Assert.That(outcome, Is.EqualTo(ConnectOutcome.ControllerNotInitialized));
            Assert.That(connector.ConnectedLayerNames, Is.Empty);
        }

        // ================================================================
        // 解放
        // ================================================================

        [UnityTest]
        public IEnumerator Disconnect_RestoresSlotsWeightsAndRegistry()
        {
            FacialProfile profile = CreateProfile(EmotionLayer);
            Host host = CreateHost(profile);
            IInputSourceRegistry registry = host.Controller.InputSourceRegistry;
            string[] idsBefore = Snapshot(registry.RegisteredIds);
            var connector = new TimelineLayerConnector(host.Controller, registry, _slug);
            connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), new FacialTimelineDiagnostics());
            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink valueSink), Is.True);
            Assert.That(connector.TryGetStateSink(EmotionLayer, out TimelineExpressionStateSink stateSink), Is.True);
            valueSink.TryGetBufferIndex("smile", out int bufferIndex);
            valueSink.SetValue(bufferIndex, 0.6f);
            stateSink.TriggerOn("smile");
            yield return null;
            Assert.That(host.Renderer.GetBlendShapeWeight(0), Is.EqualTo(60f).Within(0.01f));

            connector.Disconnect();
            yield return null;

            Assert.That(host.Controller.IsLayerInputSourceBound(EmotionLayer, ValueId), Is.False);
            Assert.That(Snapshot(registry.RegisteredIds), Is.EqualTo(idsBefore), "registry は接続前に戻ること");
            Assert.That(host.Renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.01f), "寄与が消えること");
            Assert.That(valueSink.IsValid, Is.False, "値 sink は Invalidate されること");
            Assert.That(stateSink.ActiveExpressionIds, Is.Empty, "state sink は TriggerOff されること");
            Assert.That(connector.ConnectedLayerNames, Is.Empty);

            Assert.DoesNotThrow(() => connector.Disconnect(), "二重解放は no-op");

            // 再接続で値が正しく出る（解放で slot / weight が片付いている証明）。
            Assert.That(connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer),
                new FacialTimelineDiagnostics()), Is.EqualTo(ConnectOutcome.Connected));
            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink again), Is.True);
            again.TryGetBufferIndex("smile", out int againIndex);
            again.SetValue(againIndex, 0.2f);
            yield return null;
            Assert.That(host.Renderer.GetBlendShapeWeight(0), Is.EqualTo(20f).Within(0.01f));
        }

        [Test]
        public void Connect_AfterDisconnectWithSameInputs_ReusesPooledSinks()
        {
            FacialProfile profile = CreateProfile(EmotionLayer);
            Host host = CreateHost(profile);
            FacialTimelineBakeAsset bake = CreateBake(EmotionLayer);
            var connector = new TimelineLayerConnector(host.Controller, host.Controller.InputSourceRegistry, _slug);
            connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, bake, new FacialTimelineDiagnostics());
            connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink firstValue);
            connector.TryGetStateSink(EmotionLayer, out TimelineExpressionStateSink firstState);
            connector.Disconnect();

            connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, bake, new FacialTimelineDiagnostics());

            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink secondValue), Is.True);
            Assert.That(connector.TryGetStateSink(EmotionLayer, out TimelineExpressionStateSink secondState), Is.True);
            Assert.That(secondValue, Is.SameAs(firstValue));
            Assert.That(secondState, Is.SameAs(firstState));

            connector.Disconnect();
            connector.Connect(Derive(EmotionLayer, 0), profile, BlendShapeNames, CreateBake(EmotionLayer), new FacialTimelineDiagnostics());

            Assert.That(connector.TryGetValueSink(EmotionLayer, out TimelineBakedValueSink thirdValue), Is.True);
            Assert.That(thirdValue, Is.Not.SameAs(firstValue), "Bake が変われば sink を作り直すこと");
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private sealed class Host
        {
            public FacialController Controller;
            public SkinnedMeshRenderer Renderer;
            public ProbeBinding Probe;
        }

        private Host CreateHost(FacialProfile profile)
        {
            var root = new GameObject("ConnectorHost");
            _created.Add(root);
            root.AddComponent<Animator>();

            var meshObject = new GameObject("Face");
            meshObject.transform.SetParent(root.transform, false);
            var renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMesh();

            var probe = new ProbeBinding { Slug = "probe" };
            var so = UnityEngine.ScriptableObject.CreateInstance<ProbeProfileSO>();
            _created.Add(so);
            so.WritableAdapterBindings.Add(probe);

            var controller = root.AddComponent<FacialController>();
            controller.CharacterSO = so;
            controller.InitializeWithProfile(profile);
            Assert.That(controller.IsInitialized, Is.True, "fixture: FacialController が初期化できること");
            Assert.That(controller.InputSourceRegistry, Is.Not.Null, "fixture: registry が用意されていること");

            return new Host { Controller = controller, Renderer = renderer, Probe = probe };
        }

        private Mesh CreateMesh()
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            for (int i = 0; i < BlendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(BlendShapeNames[i], 100f, new Vector3[3], null, null);
            }

            _created.Add(mesh);
            return mesh;
        }

        private FacialTimelineBakeAsset CreateBake(string layerName)
        {
            var bake = UnityEngine.ScriptableObject.CreateInstance<FacialTimelineBakeAsset>();
            _created.Add(bake);
            bake.ExpressionBakes = new[]
            {
                new ExpressionSourceBake
                {
                    LayerName = layerName,
                    Curves = new[]
                    {
                        new BlendShapeCurve { BlendShapeName = "smile", Curve = AnimationCurve.Constant(0f, 1f, 1f) },
                    },
                },
            };
            return bake;
        }

        private static FacialProfile CreateProfile(string layerName, InputSourceDeclaration[] declarations = null)
        {
            var layers = new[] { new LayerDefinition(layerName, 0, ExclusionMode.LastWins) };
            var expressions = new[]
            {
                new Expression(
                    "smile", "Smile", layerName, 0.05f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("smile", 1f) }),
            };
            InputSourceDeclaration[][] layerInputSources = declarations == null ? null : new[] { declarations };
            return new FacialProfile("1.0", layers, expressions, layerInputSources: layerInputSources);
        }

        private static TimelineDerivation Derive(string layerName, int layerIndex)
        {
            return new TimelineDerivation(
                new[] { new TimelineLayerDescriptor(layerName, layerIndex, trackIndex: 0) },
                Array.Empty<string>(),
                Array.Empty<TimelineChannelDescriptor>(),
                Array.Empty<string>(),
                hasFacialTracks: true);
        }

        private static string[] Snapshot(IReadOnlyList<string> ids)
        {
            var copy = new string[ids.Count];
            for (int i = 0; i < ids.Count; i++)
            {
                copy[i] = ids[i];
            }

            Array.Sort(copy, StringComparer.Ordinal);
            return copy;
        }

        private static TimelineDiagnosticSeverity Severity(FacialTimelineDiagnostics diagnostics, TimelineDiagnosticCode code)
        {
            foreach (TimelineDiagnosticItem item in diagnostics.Items)
            {
                if (item.Code == code)
                {
                    return item.Severity;
                }
            }

            Assert.Fail($"診断 {code} が記録されていない");
            return default;
        }

        private static string SubjectOf(FacialTimelineDiagnostics diagnostics, TimelineDiagnosticCode code)
        {
            foreach (TimelineDiagnosticItem item in diagnostics.Items)
            {
                if (item.Code == code)
                {
                    return item.Subject;
                }
            }

            Assert.Fail($"診断 {code} が記録されていない");
            return null;
        }

        /// <summary>overlay suppress の active provider（<see cref="AdapterBuildContext.ActiveExpressionProvider"/>）を捕まえる binding。</summary>
        [Serializable]
        private sealed class ProbeBinding : AdapterBindingBase
        {
            [NonSerialized] public IActiveExpressionProvider ActiveExpressionProvider;

            public override void OnStart(in AdapterBuildContext ctx)
            {
                ActiveExpressionProvider = ctx.ActiveExpressionProvider;
            }
        }

        private sealed class ProbeProfileSO : FacialCharacterProfileSO
        {
            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;
        }

        /// <summary>宣言以外の経路で state id に繋がった入力源（動的検出の再現用）。</summary>
        private sealed class StrayValueSource : ValueProviderInputSourceBase
        {
            public StrayValueSource(string id, int blendShapeCount)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
            }

            public override bool TryWriteValues(Span<float> output)
            {
                return false;
            }
        }
    }
}
