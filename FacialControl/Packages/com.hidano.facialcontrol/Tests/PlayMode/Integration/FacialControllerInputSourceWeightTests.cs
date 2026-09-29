using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject.Serializable;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    // ランタイム weight 変更 API の PlayMode 統合テスト。
    /// </summary>
    /// <remarks>
    /// 観測完了条件: メインスレッド外スレッドから <c>SetInputSourceWeight</c> を呼んでも
    /// 次フレームの <c>Aggregate</c> 観測 (BlendShape 出力) に反映されること
    /// 。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class FacialControllerInputSourceWeightTests : SizedTestFixture
    {
        private GameObject _gameObject;
        private Mesh _mesh;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }

            if (_mesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_mesh);
                _mesh = null;
            }
        }

        // ================================================================
        // 任意スレッド書込 → 次 Aggregate / BlendShape 出力で観測
        // (LayerUseCase 直結で end-to-end 検証。FacialController.SetInputSourceWeight は
        //  この LayerUseCase API への薄い forwarding なので、契約は同等)。
        // ================================================================

        [UnityTest]
        public IEnumerator SetInputSourceWeight_FromBackgroundThread_ReflectedInBlendShapeOutput()
        {
            string[] blendShapeNames = { "bs_a", "bs_b" };
            var profile = CreateLayerProfileWithExpression("emotion", "expr-1", "bs_a", value: 1.0f);
            var expressionUseCase = new ExpressionUseCase(profile);

            // sourceIdx=1 に FakeSource (bs_b に値 1.0 を書込む)。weight=0 で初期化。
            var fake = new FakeSelectiveValueSource(
                "osc", blendShapeCount: 2, writeIndex: 1, writeValue: 1.0f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fake, 0.0f)
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            try
            {
                // LayerExpressionSource (sourceIdx=0) を活性化するため Expression を Activate。
                // transitionDuration=0 のため 1 frame で値が target に到達する。
                expressionUseCase.Activate(profile.Expressions.Span[0]);
                layerUseCase.UpdateWeights(0.001f);

                var initial = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(1.0f, initial[0], 1e-4f, "前提: source0 (Expression) の bs_a 寄与が出ていること。");
                Assert.AreEqual(0.0f, initial[1], 1e-4f, "前提: source1 (FakeSource) の bs_b 寄与は weight=0 で出ないこと。");

                // 背景スレッドから FakeSource の weight を 0.7 へ書込む。
                int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                int? backgroundThreadId = null;
                var task = Task.Run(() =>
                {
                    backgroundThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    layerUseCase.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.7f);
                });
                task.Wait();
                Assert.IsNotNull(backgroundThreadId);
                Assert.AreNotEqual(mainThreadId, backgroundThreadId.Value,
                    "前提: SetInputSourceWeight は実際にメインスレッド外で呼ばれていること。");

                yield return null;

                // 次フレーム: UpdateWeights → Aggregate 入口の SwapIfDirty で書込が観測される。
                layerUseCase.UpdateWeights(0.016f);

                var next = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(1.0f, next[0], 1e-4f, "source0 の寄与 (bs_a=1.0) は維持されること。");
                Assert.AreEqual(0.7f, next[1], 1e-4f,
                    "background thread からの SetInputSourceWeight が次 BlendShape 出力に反映されること。");
            }
            finally
            {
                layerUseCase.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator BeginInputSourceWeightBatch_FromBackgroundThread_AtomicallyReflected()
        {
            string[] blendShapeNames = { "bs_a", "bs_b", "bs_c" };
            var profile = CreateLayerProfileWithExpression("emotion", "expr-1", "bs_a", value: 1.0f);
            var expressionUseCase = new ExpressionUseCase(profile);

            // sourceIdx=1 / 2 にそれぞれ別 BlendShape へ書込む FakeSource を配置。
            var fakeB = new FakeSelectiveValueSource("osc", blendShapeCount: 3, writeIndex: 1, writeValue: 1.0f);
            var fakeC = new FakeSelectiveValueSource("lipsync", blendShapeCount: 3, writeIndex: 2, writeValue: 1.0f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fakeB, 0.0f),
                (0, fakeC, 0.0f)
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            try
            {
                expressionUseCase.Activate(profile.Expressions.Span[0]);
                layerUseCase.UpdateWeights(0.001f);

                // 背景スレッドからバルクスコープで 2 つの weight をまとめて書込む。
                var task = Task.Run(() =>
                {
                    using (var batch = layerUseCase.BeginInputSourceWeightBatch())
                    {
                        batch.SetWeight(0, 1, 0.4f);
                        batch.SetWeight(0, 2, 0.3f);
                    }
                });
                task.Wait();

                yield return null;

                layerUseCase.UpdateWeights(0.016f);
                var output = layerUseCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f, "source0 (Expression) の寄与は維持されること。");
                Assert.AreEqual(0.4f, output[1], 1e-4f, "FakeB の weight=0.4 が bs_b に反映されること。");
                Assert.AreEqual(0.3f, output[2], 1e-4f, "FakeC の weight=0.3 が bs_c に反映されること。");
            }
            finally
            {
                layerUseCase.Dispose();
            }
        }

        // ================================================================
        // FacialController 経由 API の forwarding（最終 BlendShape 出力で観測）
        //
        // 1 BlendShape ("bs_a") を持つ renderer と、常に 1.0 を書き込む mock source を
        // 宣言 weight=0 で layer に登録する。weight を変更すると次フレームの LateUpdate
        // （Aggregate → 出力ライター）で renderer の BlendShape weight (0..100) に反映される。
        // ================================================================

        [UnityTest]
        public IEnumerator FacialController_SetInputSourceWeight_FromBackgroundThread_ReflectedInRendererWeight()
        {
            _gameObject = CreateGameObjectWithAnimatorAndBlendShapeRenderer("bs_a");
            var renderer = _gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            var controller = _gameObject.AddComponent<FacialController>();
            var so = CreateSOWithBindingDeclaration("mock", declaredWeight: 0f);
            controller.CharacterSO = so;
            controller.Initialize();

            try
            {
                yield return null;
                Assert.IsTrue(controller.IsInitialized);
                Assert.AreEqual(0f, renderer.GetBlendShapeWeight(0), 1e-3f,
                    "前提: 宣言 weight=0 のため mock source の寄与は BlendShape 出力に出ないこと。");

                int mainThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                int? backgroundThreadId = null;
                var task = Task.Run(() =>
                {
                    backgroundThreadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
                    controller.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.42f);
                });
                task.Wait();
                Assert.IsNotNull(backgroundThreadId);
                Assert.AreNotEqual(mainThreadId, backgroundThreadId.Value,
                    "前提: SetInputSourceWeight は実際にメインスレッド外で呼ばれていること。");

                yield return null;

                Assert.AreEqual(42f, renderer.GetBlendShapeWeight(0), 1e-2f,
                    "FacialController.SetInputSourceWeight が次フレームの BlendShape 出力 (1.0 * 0.42 → 42) に反映されること。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        [UnityTest]
        public IEnumerator FacialController_BeginInputSourceWeightBatch_AfterInit_ReflectedInRendererWeight()
        {
            _gameObject = CreateGameObjectWithAnimatorAndBlendShapeRenderer("bs_a");
            var renderer = _gameObject.GetComponentInChildren<SkinnedMeshRenderer>();
            var controller = _gameObject.AddComponent<FacialController>();
            var so = CreateSOWithBindingDeclaration("mock", declaredWeight: 0f);
            controller.CharacterSO = so;
            controller.Initialize();

            try
            {
                yield return null;
                Assert.IsTrue(controller.IsInitialized);
                Assert.AreEqual(0f, renderer.GetBlendShapeWeight(0), 1e-3f,
                    "前提: 宣言 weight=0 のため mock source の寄与は BlendShape 出力に出ないこと。");

                using (var batch = controller.BeginInputSourceWeightBatch())
                {
                    batch.SetWeight(0, 1, 0.55f);
                }

                yield return null;

                Assert.AreEqual(55f, renderer.GetBlendShapeWeight(0), 1e-2f,
                    "FacialController.BeginInputSourceWeightBatch 経由の書込が次フレームの BlendShape 出力 (1.0 * 0.55 → 55) に反映されること。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(so);
            }
        }

        // ================================================================
        // Fakes / ヘルパー
        // ================================================================

        /// <summary>
        /// 指定 index の BlendShape にだけ固定値を書込む値提供型フェイク。
        /// 他の index は呼出側のクリア状態 (ゼロ) のまま残す。
        /// </summary>
        private sealed class FakeSelectiveValueSource : ValueProviderInputSourceBase
        {
            private readonly int _writeIndex;
            private readonly float _writeValue;

            public FakeSelectiveValueSource(string id, int blendShapeCount, int writeIndex, float writeValue)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _writeIndex = writeIndex;
                _writeValue = writeValue;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                if ((uint)_writeIndex < (uint)output.Length)
                {
                    output[_writeIndex] = _writeValue;
                }
                return true;
            }
        }

        private static FacialProfile CreateLayerProfileWithExpression(
            string layerName, string expressionId, string blendShapeName, float value)
        {
            var layers = new[]
            {
                new LayerDefinition(layerName, 0, ExclusionMode.LastWins)
            };
            var expressions = new[]
            {
                new Expression(
                    expressionId, expressionId, layerName,
                    transitionDuration: 0f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[] { new BlendShapeMapping(blendShapeName, value) })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        /// <summary>
        /// 単一 layer + Mock binding 1 個（slug = <paramref name="slug"/>）の <see cref="FacialCharacterProfileSO"/>
        /// を構築する。layer.inputSources[0] には slug をそのまま id として宣言 weight
        /// <paramref name="declaredWeight"/> で宣言し、FacialController が child scope の
        /// <see cref="IInputSourceRegistry"/> 経由で sourceIdx=1 として登録するようにする。
        /// mock source は全 BlendShape に 1.0 を書き込むため、出力値 = 宣言/ランタイム weight となる。
        /// </summary>
        private static MockBindingProfileSO CreateSOWithBindingDeclaration(string slug, float declaredWeight)
        {
            var so = ScriptableObject.CreateInstance<MockBindingProfileSO>();
            so.LayerName = "emotion";
            so.LayerInputSourceId = slug;
            so.LayerInputSourceWeight = declaredWeight;
            so.WritableAdapterBindings.Add(new ConstantValueAdapterBinding(slug, blendShapeCount: 1, value: 1.0f)
            {
                Slug = slug
            });
            return so;
        }

        /// <summary>
        /// テスト用 SO。<see cref="LoadProfile"/> で 1 layer + 1 inputSources 宣言の最小プロファイルを返す。
        /// </summary>
        public sealed class MockBindingProfileSO : FacialCharacterProfileSO
        {
            public string LayerName = "emotion";
            public string LayerInputSourceId;
            public float LayerInputSourceWeight = 1.0f;

            public List<AdapterBindingBase> WritableAdapterBindings => _adapterBindings;

            public override FacialProfile LoadProfile()
            {
                var layers = new[]
                {
                    new LayerDefinition(LayerName, 0, ExclusionMode.LastWins)
                };
                var layerInputSources = new InputSourceDeclaration[][]
                {
                    new InputSourceDeclaration[]
                    {
                        new InputSourceDeclaration(LayerInputSourceId, LayerInputSourceWeight, null)
                    }
                };
                return new FacialProfile("2.0", layers, null, null, layerInputSources);
            }
        }

        /// <summary>
        /// <see cref="OnStart"/> で slug を primary id として <see cref="ConstantValueInputSource"/> を
        /// <see cref="IInputSourceRegistry"/> に登録する Mock binding。
        /// </summary>
        [Serializable]
        public sealed class ConstantValueAdapterBinding : AdapterBindingBase
        {
            [NonSerialized] private readonly string _id;
            [NonSerialized] private readonly int _blendShapeCount;
            [NonSerialized] private readonly float _value;

            public ConstantValueAdapterBinding(string id, int blendShapeCount, float value)
            {
                _id = id;
                _blendShapeCount = blendShapeCount;
                _value = value;
            }

            public override void OnStart(in AdapterBuildContext ctx)
            {
                var slug = AdapterSlug.Parse(Slug);
                ctx.InputSourceRegistry.Register(
                    slug, new ConstantValueInputSource(_id, _blendShapeCount, _value));
            }
        }

        /// <summary>全 BlendShape index に固定値を書き込む値提供型フェイク。</summary>
        private sealed class ConstantValueInputSource : ValueProviderInputSourceBase
        {
            private readonly float _value;

            public ConstantValueInputSource(string id, int blendShapeCount, float value)
                : base(InputSourceId.Parse(id), blendShapeCount)
            {
                _value = value;
            }

            public override bool TryWriteValues(Span<float> output)
            {
                for (int i = 0; i < output.Length; i++)
                {
                    output[i] = _value;
                }
                return true;
            }
        }

        private GameObject CreateGameObjectWithAnimatorAndBlendShapeRenderer(string blendShapeName)
        {
            var go = new GameObject("FacialControllerInputSourceWeightTest");
            go.AddComponent<Animator>();
            var childObj = new GameObject("Mesh");
            childObj.transform.SetParent(go.transform);
            var renderer = childObj.AddComponent<SkinnedMeshRenderer>();

            _mesh = new Mesh();
            _mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            _mesh.triangles = new[] { 0, 1, 2 };
            _mesh.AddBlendShapeFrame(blendShapeName, 100f, new Vector3[3], null, null);
            renderer.sharedMesh = _mesh;
            return go;
        }
    }
}
