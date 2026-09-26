using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.Playable;
using Hidano.FacialControl.Adapters.ScriptableObject;
using Hidano.FacialControl.Domain.Models;

namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    /// P14-T04: 複数 SkinnedMeshRenderer の統合テスト。
    /// 複数の Renderer を持つキャラクターでの FacialController 動作を検証する。
    /// </summary>
    [TestFixture]
    public class MultiRendererTests
    {
        private GameObject _gameObject;

        [TearDown]
        public void TearDown()
        {
            if (_gameObject != null)
            {
                var renderers = _gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
                for (int i = 0; i < renderers.Length; i++)
                {
                    if (renderers[i] != null && renderers[i].sharedMesh != null)
                    {
                        Object.DestroyImmediate(renderers[i].sharedMesh);
                    }
                }

                Object.DestroyImmediate(_gameObject);
                _gameObject = null;
            }
        }

        // ================================================================
        // 自動検索テスト
        // ================================================================

        [Test]
        public void AutoDetect_MultipleChildRenderers_InitializesSuccessfully()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(3);
            var controller = _gameObject.AddComponent<FacialController>();

            var profile = CreateTestProfile();
            controller.InitializeWithProfile(profile);

            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void AutoDetect_NestedChildRenderers_DetectedAutomatically()
        {
            _gameObject = new GameObject("MultiRendererTest");
            _gameObject.AddComponent<Animator>();

            // ネストされた子オブジェクト構造
            var body = new GameObject("Body");
            body.transform.SetParent(_gameObject.transform);
            var bodyChild = new GameObject("BodyMesh");
            bodyChild.transform.SetParent(body.transform);
            bodyChild.AddComponent<SkinnedMeshRenderer>();

            var face = new GameObject("Face");
            face.transform.SetParent(_gameObject.transform);
            var faceChild = new GameObject("FaceMesh");
            faceChild.transform.SetParent(face.transform);
            faceChild.AddComponent<SkinnedMeshRenderer>();

            var controller = _gameObject.AddComponent<FacialController>();
            var profile = CreateTestProfile();
            controller.InitializeWithProfile(profile);

            Assert.IsTrue(controller.IsInitialized);
        }

        // ================================================================
        // 手動オーバーライドテスト
        // ================================================================

        [Test]
        public void ManualOverride_SpecificRenderers_UsesOnlyOverrideList()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(3);
            var controller = _gameObject.AddComponent<FacialController>();

            // 手動で特定の Renderer のみを指定
            var allRenderers = _gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            controller.SkinnedMeshRenderers = new SkinnedMeshRenderer[] { allRenderers[0] };

            var profile = CreateTestProfile();
            controller.InitializeWithProfile(profile);

            Assert.IsTrue(controller.IsInitialized);
        }

        [Test]
        public void ManualOverride_EmptyArray_FallsBackToAutoDetect()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(2);
            var controller = _gameObject.AddComponent<FacialController>();
            controller.SkinnedMeshRenderers = new SkinnedMeshRenderer[0];

            var profile = CreateTestProfile();
            controller.InitializeWithProfile(profile);

            Assert.IsTrue(controller.IsInitialized);
        }

        // ================================================================
        // 複数 Renderer での Expression アクティブ化テスト
        // ================================================================

        [Test]
        public void MultipleRenderers_ActivateExpression_Succeeds()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(2);
            var controller = _gameObject.AddComponent<FacialController>();

            var profile = CreateProfileWithExpressions();
            controller.InitializeWithProfile(profile);

            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Happy", active[0].Name);
        }

        [UnityTest]
        public IEnumerator MultipleRenderers_LateUpdate_AppliesBlendShapeViaWriter()
        {
            _gameObject = new GameObject("WriterApplyTest");
            _gameObject.AddComponent<Animator>();

            CreateRendererWithBlendShapeMesh(_gameObject.transform, "Face", "smile");
            CreateRendererWithBlendShapeMesh(_gameObject.transform, "Teeth", "smile");

            var controller = _gameObject.AddComponent<FacialController>();
            var profile = CreateProfileWithExpressions();
            controller.InitializeWithProfile(profile);
            controller.Activate(profile.Expressions.Span[0]);

            yield return null;

            var renderers = _gameObject.GetComponentsInChildren<SkinnedMeshRenderer>();
            Assert.That(renderers, Has.Length.EqualTo(2));
            Assert.That(renderers[0].GetBlendShapeWeight(0), Is.GreaterThan(0f));
            Assert.That(renderers[1].GetBlendShapeWeight(0), Is.EqualTo(renderers[0].GetBlendShapeWeight(0)).Within(0.0001f));
        }

        [Test]
        public void MultipleRenderers_ActivateAndDeactivate_WorksCorrectly()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(3);
            var controller = _gameObject.AddComponent<FacialController>();

            var profile = CreateProfileWithExpressions();
            controller.InitializeWithProfile(profile);

            var happy = profile.Expressions.Span[0];
            var sad = profile.Expressions.Span[1];

            controller.Activate(happy);
            controller.Activate(sad);
            // LastWins → sad のみ
            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
            Assert.AreEqual("Sad", active[0].Name);

            controller.Deactivate(sad);
            active = controller.GetActiveExpressions();
            Assert.AreEqual(0, active.Count);
        }

        // ================================================================
        // プロファイル切替（複数 Renderer）テスト
        // ================================================================

        [Test]
        public void MultipleRenderers_ProfileSwitch_ReinitializesCorrectly()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(2);
            var controller = _gameObject.AddComponent<FacialController>();

            var profile1 = CreateProfileWithExpressions();
            controller.InitializeWithProfile(profile1);

            Assert.IsTrue(controller.IsInitialized);

            // 新しいプロファイルに切替
            var profile2 = CreateAlternativeProfile();
            controller.InitializeWithProfile(profile2);

            Assert.IsTrue(controller.IsInitialized);
            Assert.AreEqual(0, controller.GetActiveExpressions().Count);
        }

        // ================================================================
        // Renderer なしのケース
        // ================================================================

        [Test]
        public void NoRenderer_Initialize_StillInitializes()
        {
            _gameObject = new GameObject("NoRendererTest");
            _gameObject.AddComponent<Animator>();

            var controller = _gameObject.AddComponent<FacialController>();
            var profile = CreateTestProfile();
            controller.InitializeWithProfile(profile);

            // Renderer がなくても InitializeWithProfile は成功する
            // （BlendShape 名が空になるだけ）
            Assert.IsTrue(controller.IsInitialized);
        }

        // ================================================================
        // ライフサイクル（複数 Renderer）テスト
        // ================================================================

        [UnityTest]
        public IEnumerator MultipleRenderers_DisableAndReenable_ResourcesCleanedUp()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(3);
            var controller = _gameObject.AddComponent<FacialController>();

            var profile = CreateProfileWithExpressions();
            controller.InitializeWithProfile(profile);

            yield return null;
            Assert.IsTrue(controller.IsInitialized);

            // 無効化
            controller.enabled = false;
            yield return null;
            Assert.IsFalse(controller.IsInitialized);

            // 再有効化して再初期化
            controller.enabled = true;
            yield return null;
            controller.InitializeWithProfile(profile);

            Assert.IsTrue(controller.IsInitialized);

            var expression = profile.Expressions.Span[0];
            controller.Activate(expression);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(1, active.Count);
        }

        // ================================================================
        // マルチレイヤー + 複数 Renderer 統合テスト
        // ================================================================

        [Test]
        public void MultipleRenderers_MultiLayer_ExpressionsOnDifferentLayers()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(2);
            var controller = _gameObject.AddComponent<FacialController>();

            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("lipsync", 1, ExclusionMode.Blend),
                new LayerDefinition("eye", 2, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("happy", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("smile", 1.0f) }),
                new Expression("lip-a", "LipA", "lipsync", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("mouth_a", 0.8f) }),
                new Expression("wink", "Wink", "eye", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("eye_wink", 1.0f) })
            };
            var profile = new FacialProfile("1.0.0", layers, expressions);
            controller.InitializeWithProfile(profile);

            controller.Activate(expressions[0]); // emotion
            controller.Activate(expressions[1]); // lipsync
            controller.Activate(expressions[2]); // eye

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(3, active.Count);
        }

        // ================================================================
        // Blend モード + 複数 Renderer 統合テスト
        // ================================================================

        [Test]
        public void MultipleRenderers_BlendMode_MultipleExpressionsCoexist()
        {
            _gameObject = CreateGameObjectWithMultipleRenderers(2);
            var controller = _gameObject.AddComponent<FacialController>();

            var layers = new LayerDefinition[]
            {
                new LayerDefinition("lipsync", 0, ExclusionMode.Blend)
            };
            var expressions = new Expression[]
            {
                new Expression("lip-a", "LipA", "lipsync", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("mouth_a", 1.0f) }),
                new Expression("lip-o", "LipO", "lipsync", 0.1f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[] { new BlendShapeMapping("mouth_o", 0.8f) })
            };
            var profile = new FacialProfile("1.0.0", layers, expressions);
            controller.InitializeWithProfile(profile);

            controller.Activate(expressions[0]);
            controller.Activate(expressions[1]);

            var active = controller.GetActiveExpressions();
            Assert.AreEqual(2, active.Count);
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static GameObject CreateGameObjectWithMultipleRenderers(int rendererCount)
        {
            var go = new GameObject("MultiRendererTest");
            go.AddComponent<Animator>();

            for (int i = 0; i < rendererCount; i++)
            {
                var child = new GameObject($"Mesh_{i}");
                child.transform.SetParent(go.transform);
                child.AddComponent<SkinnedMeshRenderer>();
            }

            return go;
        }

        private static SkinnedMeshRenderer CreateRendererWithBlendShapeMesh(Transform parent, string name, string blendShapeName)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent);

            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = CreateMeshWithBlendShape(blendShapeName);
            return renderer;
        }

        private static Mesh CreateMeshWithBlendShape(string blendShapeName)
        {
            var mesh = new Mesh();
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.triangles = new[] { 0, 1, 2 };
            mesh.AddBlendShapeFrame(blendShapeName, 100f, new Vector3[3], null, null);
            return mesh;
        }

        private static FacialProfile CreateTestProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            return new FacialProfile("1.0.0", layers);
        }

        private static FacialProfile CreateProfileWithExpressions()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins)
            };
            var expressions = new Expression[]
            {
                new Expression("expr-happy", "Happy", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("smile", 1.0f)
                    }),
                new Expression("expr-sad", "Sad", "emotion", 0.25f,
                    TransitionCurve.Linear,
                    new BlendShapeMapping[]
                    {
                        new BlendShapeMapping("frown", 0.8f)
                    })
            };
            return new FacialProfile("1.0.0", layers, expressions);
        }

        private static FacialProfile CreateAlternativeProfile()
        {
            var layers = new LayerDefinition[]
            {
                new LayerDefinition("emotion", 0, ExclusionMode.LastWins),
                new LayerDefinition("eye", 1, ExclusionMode.LastWins)
            };
            return new FacialProfile("2.0.0", layers);
        }
    }
}
