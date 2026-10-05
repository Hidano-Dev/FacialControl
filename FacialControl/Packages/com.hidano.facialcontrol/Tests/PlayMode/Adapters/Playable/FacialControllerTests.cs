using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Adapters.Playable
{
    /// <summary>
    /// <see cref="FacialController"/> の公開 API のうち、宣言（<c>Layer.inputSources</c>）の無い入力源を
    /// レイヤーへ後付けで接続 / 解放 / 判定する口と、系2 入力源を観測へ登録する口を検証する。
    /// Aggregate への反映は実際の LateUpdate を 1 フレーム進めて SkinnedMeshRenderer の BlendShape 値で確認するため
    /// PlayMode に置く（EditMode では MonoBehaviour の LateUpdate が走らない）。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class FacialControllerTests : SizedTestFixture
    {
        private const string EmotionLayer = "emotion";
        private const string TimelineSinkId = "timeline:emotion";
        private const string TimelineStateId = "timeline:emotion:state";
        private const string DeclaredLiveId = "fake:live";
        private static readonly string[] BlendShapeNames = { "smile", "frown" };

        private readonly List<UnityEngine.Object> _created = new List<UnityEngine.Object>();

        [SetUp]
        public void SetUp()
        {
            FacialControllerRendererOwnership.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            FacialControllerRendererOwnership.Clear();

            for (int i = 0; i < _created.Count; i++)
            {
                if (_created[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_created[i]);
                }
            }
            _created.Clear();
        }

        // ================================================================
        // 未初期化 / 引数不正
        // ================================================================

        [Test]
        public void LateBindApis_NotInitialized_ReturnFalse()
        {
            var profile = CreateProfile();
            var controller = CreateUninitializedController();
            var valueSource = new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.6f);
            var stateSource = new FakeTriggerSource(TimelineStateId, profile);

            Assert.That(controller.IsInitialized, Is.False);
            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, valueSource, 1f), Is.False);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, TimelineSinkId), Is.False);
            Assert.That(controller.UnbindLayerInputSource(EmotionLayer, TimelineSinkId), Is.False);
            Assert.That(controller.TryRegisterLayerStateSource(EmotionLayer, TimelineStateId, stateSource), Is.False);
            Assert.That(controller.UnregisterLayerStateSource(EmotionLayer, TimelineStateId), Is.False);
        }

        [Test]
        public void TryBindLayerInputSource_InvalidArguments_ReturnsFalse()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out _);
            var valueSource = new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.6f);

            Assert.That(controller.TryBindLayerInputSource("no-such-layer", TimelineSinkId, valueSource, 1f), Is.False,
                "プロファイルに無いレイヤー名は false");
            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, "bad id!", valueSource, 1f), Is.False,
                "InputSourceId 規約を満たさない id は false");
            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, null, 1f), Is.False,
                "null source は false");
            Assert.That(controller.IsLayerInputSourceBound("no-such-layer", TimelineSinkId), Is.False);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, TimelineSinkId), Is.False,
                "失敗した Bind は接続を残さないこと");
        }

        [Test]
        public void TryRegisterLayerStateSource_InvalidArguments_ReturnsFalse()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out _);
            var stateSource = new FakeTriggerSource(TimelineStateId, profile);

            Assert.That(controller.TryRegisterLayerStateSource("no-such-layer", TimelineStateId, stateSource), Is.False);
            Assert.That(controller.TryRegisterLayerStateSource(EmotionLayer, "bad id!", stateSource), Is.False);
            Assert.That(controller.TryRegisterLayerStateSource(EmotionLayer, TimelineStateId, null), Is.False);
            Assert.That(controller.UnregisterLayerStateSource(EmotionLayer, TimelineStateId), Is.False,
                "未登録の状態入力源の解除は false");
        }

        // ================================================================
        // 値入力源の接続 / 解放
        // ================================================================

        [UnityTest]
        public IEnumerator TryBindLayerInputSource_ValueSource_ValueReachesBlendShape()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out SkinnedMeshRenderer renderer);
            var valueSource = new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.6f);

            bool bound = controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, valueSource, 1f);

            Assert.That(bound, Is.True);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, TimelineSinkId), Is.True);

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(60f).Within(0.01f),
                "接続した値入力源の書込値（0.6）が Aggregate を経て BlendShape（0..100）へ届くこと");
            Assert.That(renderer.GetBlendShapeWeight(1), Is.EqualTo(0f).Within(0.01f),
                "入力源が書かない index は影響を受けないこと");
        }

        [UnityTest]
        public IEnumerator TryBindLayerInputSource_WeightHalf_ScalesContribution()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out SkinnedMeshRenderer renderer);
            var valueSource = new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.6f);

            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, valueSource, 0.5f), Is.True);

            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(30f).Within(0.01f),
                "指定 weight がレイヤー内加重の初期値として焼かれること (0.6 * 0.5)");
        }

        [UnityTest]
        public IEnumerator UnbindLayerInputSource_AfterBind_RestoresOutputAndSlots()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out SkinnedMeshRenderer renderer);
            var valueSource = new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.6f);

            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, valueSource, 1f), Is.True);
            yield return null;
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(60f).Within(0.01f));

            bool unbound = controller.UnbindLayerInputSource(EmotionLayer, TimelineSinkId);
            yield return null;

            Assert.That(unbound, Is.True);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, TimelineSinkId), Is.False,
                "解放後はスロットが外れて未接続に戻ること");
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(0f).Within(0.01f),
                "解放後は入力源の寄与が消えて出力が元に戻ること");

            // 同じ id を再接続できること（解放でスロットと weight が正しく片付いている証明）。
            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId,
                new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.2f), 1f), Is.True);
            yield return null;
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(20f).Within(0.01f),
                "再接続後は新しい入力源の値だけが出ること（旧 weight が残っていない）");
        }

        [Test]
        public void UnbindLayerInputSource_NotBound_ReturnsFalse()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out _);

            Assert.That(controller.UnbindLayerInputSource(EmotionLayer, TimelineSinkId), Is.False);
        }

        [UnityTest]
        public IEnumerator TryBindLayerInputSource_DeclaredBindingResult_IsUnchanged()
        {
            // 宣言経路（Layer.inputSources + registry 購読）で接続された入力源の slot / weight が、
            // 後付け API の接続・解放で変化しないこと。
            var profile = CreateProfile(new[] { new InputSourceDeclaration(DeclaredLiveId, 0.8f, null) });
            var controller = CreateInitializedController(profile, out SkinnedMeshRenderer renderer);
            var liveSource = new FakeValueWritingSource("fake", BlendShapeNames.Length, 0.5f);
            var timelineSource = new FakeValueWritingSource(TimelineSinkId, BlendShapeNames.Length, 0.3f);

            controller.InputSourceRegistry.Register(AdapterSlug.Parse("fake"), "live", liveSource);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, DeclaredLiveId), Is.True,
                "宣言経路で後付けされた入力源も接続済みと判定されること");

            yield return null;
            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.01f), "宣言 weight 0.8 * 0.5");

            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, timelineSource, 1f), Is.True);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(70f).Within(0.01f),
                "宣言 binding の寄与（0.5 * 0.8）が後付け接続（0.3 * 1.0）で変わらず加算されること");
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, DeclaredLiveId), Is.True);

            Assert.That(controller.UnbindLayerInputSource(EmotionLayer, TimelineSinkId), Is.True);
            yield return null;

            Assert.That(renderer.GetBlendShapeWeight(0), Is.EqualTo(40f).Within(0.01f),
                "後付け分を解放しても宣言 binding の slot / weight は元のまま残ること");
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, DeclaredLiveId), Is.True);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, TimelineSinkId), Is.False);
        }

        [Test]
        public void TryBindLayerInputSource_TriggerSource_ObservedUntilUnbind()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out _);
            var trigger = new FakeTriggerSource(TimelineSinkId, profile);
            var observer = new RecordingInputObserver();
            controller.InputObservationBus.Subscribe(observer);

            Assert.That(controller.TryBindLayerInputSource(EmotionLayer, TimelineSinkId, trigger, 1f), Is.True);
            trigger.TriggerOn("smile");

            Assert.That(observer.TriggerOns, Is.EqualTo(new[] { (TimelineSinkId, "smile") }),
                "後付け接続した系2 入力源のトリガーが観測バスへ届くこと");

            Assert.That(controller.UnbindLayerInputSource(EmotionLayer, TimelineSinkId), Is.True);
            trigger.TriggerOff("smile");

            Assert.That(observer.TriggerOffs, Is.Empty, "解放後は観測されないこと");
        }

        // ================================================================
        // 状態入力源（系2）の登録 / 解除
        // ================================================================

        [Test]
        public void TryRegisterLayerStateSource_Registered_ObservedButNotBoundToLayer()
        {
            var profile = CreateProfile();
            var controller = CreateInitializedController(profile, out _);
            var stateSource = new FakeTriggerSource(TimelineStateId, profile);
            var observer = new RecordingInputObserver();
            controller.InputObservationBus.Subscribe(observer);

            bool registered = controller.TryRegisterLayerStateSource(EmotionLayer, TimelineStateId, stateSource);

            Assert.That(registered, Is.True);
            Assert.That(controller.IsLayerInputSourceBound(EmotionLayer, TimelineStateId), Is.False,
                "状態入力源はレイヤー入力源（Aggregator）には接続されないこと");

            stateSource.TriggerOn("smile");
            Assert.That(observer.TriggerOns, Is.EqualTo(new[] { (TimelineStateId, "smile") }),
                "登録した状態入力源のトリガーが観測バスへ届くこと");

            bool unregistered = controller.UnregisterLayerStateSource(EmotionLayer, TimelineStateId);
            stateSource.TriggerOff("smile");

            Assert.That(unregistered, Is.True);
            Assert.That(observer.TriggerOffs, Is.Empty, "解除後は観測されないこと");
            Assert.That(controller.UnregisterLayerStateSource(EmotionLayer, TimelineStateId), Is.False,
                "二重解除は false");
        }

        // ================================================================
        // BlendShape 名の収集
        // ================================================================

        [Test]
        public void CollectBlendShapeNames_MultipleRenderers_DeduplicatesPreservingOrder()
        {
            var first = CreateRenderer("a", "b");
            var second = CreateRenderer("b", "c");

            string[] names = FacialController.CollectBlendShapeNames(new[] { first, null, second });

            Assert.That(names, Is.EqualTo(new[] { "a", "b", "c" }));
        }

        [Test]
        public void CollectBlendShapeNames_NullOrEmpty_ReturnsEmpty()
        {
            Assert.That(FacialController.CollectBlendShapeNames(null), Is.Empty);
            Assert.That(FacialController.CollectBlendShapeNames(Array.Empty<SkinnedMeshRenderer>()), Is.Empty);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private FacialController CreateUninitializedController()
        {
            var root = new GameObject("FacialControllerTestsHost");
            _created.Add(root);
            root.AddComponent<Animator>();
            return root.AddComponent<FacialController>();
        }

        private FacialController CreateInitializedController(FacialProfile profile, out SkinnedMeshRenderer renderer)
        {
            var root = new GameObject("FacialControllerTestsHost");
            _created.Add(root);
            root.AddComponent<Animator>();

            var meshObject = new GameObject("Face");
            meshObject.transform.SetParent(root.transform, false);
            renderer = meshObject.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMesh(BlendShapeNames);

            var controller = root.AddComponent<FacialController>();
            controller.InitializeWithProfile(profile);
            Assert.That(controller.IsInitialized, Is.True, "fixture: FacialController が初期化できること");
            return controller;
        }

        private SkinnedMeshRenderer CreateRenderer(params string[] blendShapeNames)
        {
            var go = new GameObject("Renderer");
            _created.Add(go);
            var renderer = go.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMesh(blendShapeNames);
            return renderer;
        }

        private Mesh CreateMesh(string[] blendShapeNames)
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 },
            };
            for (int i = 0; i < blendShapeNames.Length; i++)
            {
                mesh.AddBlendShapeFrame(blendShapeNames[i], 100f, new Vector3[3], null, null);
            }
            _created.Add(mesh);
            return mesh;
        }

        private static FacialProfile CreateProfile(InputSourceDeclaration[] emotionDeclarations = null)
        {
            var layers = new[] { new LayerDefinition(EmotionLayer, 0, ExclusionMode.LastWins) };
            var expressions = new[]
            {
                new Expression(
                    "smile", "Smile", EmotionLayer, 0.05f, TransitionCurve.Linear,
                    new[] { new BlendShapeMapping("smile", 1f) }),
            };
            InputSourceDeclaration[][] layerInputSources = emotionDeclarations == null
                ? null
                : new[] { emotionDeclarations };
            return new FacialProfile("1.0", layers, expressions, layerInputSources: layerInputSources);
        }

        /// <summary>index 0 に固定値を書く値提供型の Fake（Timeline の値 sink 相当）。</summary>
        private sealed class FakeValueWritingSource : ValueProviderInputSourceBase
        {
            private readonly float _value;

            public FakeValueWritingSource(string id, int blendShapeCount, float value)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _value = value;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                if (output.Length > 0)
                {
                    output[0] = _value;
                }
                return true;
            }
        }

        /// <summary>系2 の Fake（Timeline の state sink 相当）。</summary>
        private sealed class FakeTriggerSource : ExpressionTriggerInputSourceBase
        {
            public FakeTriggerSource(string id, FacialProfile profile)
                : base(
                    InputSourceId.Parse(id),
                    BlendShapeNames.Length,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: BlendShapeNames,
                    profile: profile)
            {
            }
        }

        private sealed class RecordingInputObserver : IFacialInputObserver
        {
            public List<(string sourceId, string expressionId)> TriggerOns { get; } = new List<(string, string)>();
            public List<(string sourceId, string expressionId)> TriggerOffs { get; } = new List<(string, string)>();

            public void OnTriggerOn(string sourceId, string expressionId) => TriggerOns.Add((sourceId, expressionId));
            public void OnTriggerOff(string sourceId, string expressionId) => TriggerOffs.Add((sourceId, expressionId));
            public void OnAnalogSample(string sourceId, ReadOnlySpan<float> axes) { }
            public void OnValueProviderSample(string sourceId, in ValueProviderSample sample) { }
            public void OnExpressionActivated(string sourceId, string expressionId) { }
            public void OnExpressionDeactivated(string sourceId, string expressionId) { }
            public void OnLayerWeightSample(string layerName, float weight) { }
            public void OnInputSourceWeightSample(string layerName, string slotId, float weight) { }
        }
    }
}
