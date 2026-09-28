using System;
using System.Collections.Generic;
using System.Reflection;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using Hidano.FacialControl.ExpressionCreator;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.ExpressionCreator.Tests.EditMode
{
    /// <summary>
    /// <see cref="ExpressionCreatorWindow"/> の smoke テストと、
    /// UI に依存しないベイクロジック <see cref="ExpressionClipBakery"/> のテスト。
    /// Window 側は「CreateGUI が例外なく構築できる」「SaveChanges でベイクが通り hasUnsavedChanges が
    /// 落ちる」「破棄で例外を出さない」だけを守り、UI ツリーの細部は検証しない。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class ExpressionCreatorWindowTests : SizedTestFixture
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();
        private readonly List<string> _trackedAssetPaths = new List<string>();

        [TearDown]
        public void TearDown()
        {
            for (int i = 0; i < _trackedObjects.Count; i++)
            {
                if (_trackedObjects[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_trackedObjects[i]);
                }
            }
            _trackedObjects.Clear();

            for (int i = 0; i < _trackedAssetPaths.Count; i++)
            {
                AssetDatabase.DeleteAsset(_trackedAssetPaths[i]);
            }
            _trackedAssetPaths.Clear();
        }

        // ====================================================================
        // Window smoke
        // ====================================================================

        [Test]
        public void CreateGUI_NewWindow_BuildsRootWithoutThrowing()
        {
            var window = CreateWindow();

            Assert.DoesNotThrow(() => InvokeCreateGUI(window));

            Assert.That(window.rootVisualElement.childCount, Is.GreaterThan(0));
        }

        [Test]
        public void DestroyImmediate_AfterCreateGUI_DoesNotThrow()
        {
            var window = CreateWindow();
            InvokeCreateGUI(window);
            _trackedObjects.Remove(window);

            Assert.DoesNotThrow(() => UnityEngine.Object.DestroyImmediate(window));
        }

        [Test]
        public void SaveChanges_WithTargetClip_BakesAndClearsHasUnsavedChanges()
        {
            var window = CreateWindow();
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);
            InvokePrivateMethod(window, "MarkUnsavedChanges");
            Assert.That(window.hasUnsavedChanges, Is.True, "前提: 未保存変更あり。");

            var assetPath = $"Assets/expression-creator-save-{Guid.NewGuid():N}.anim";
            _trackedAssetPaths.Add(assetPath);
            var clip = new AnimationClip();
            AssetDatabase.CreateAsset(clip, assetPath);
            SetPrivateField(window, "_targetClip", clip);

            window.SaveChanges();

            Assert.That(window.hasUnsavedChanges, Is.False);
        }

        [Test]
        public void SaveChanges_NoTargetClipAndDialogCancelled_Throws()
        {
            var window = CreateWindow();
            InvokeCreateGUI(window);

            var model = CreateModelWithBlendShapes(("Face", new[] { "Smile" }));
            InvokePrivateMethod(window, "ApplyModelChange", model);
            InvokePrivateMethod(window, "MarkUnsavedChanges");

            SetPrivateField(window, "_createClipPathProvider", (Func<string>)(() => string.Empty));

            Assert.Throws<InvalidOperationException>(() => window.SaveChanges());
            Assert.That(window.hasUnsavedChanges, Is.True);
        }

        // ====================================================================
        // ExpressionClipBakery（UI 非依存の純粋ロジック）
        // ====================================================================

        [Test]
        public void Bake_BlendShapeSliders_WritesEditorCurves()
        {
            var clip = CreateTrackedClip();
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.5f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Anger", 0.25f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Head", "Surprise", 1.0f),
            };

            ExpressionClipBakery.Bake(clip, entries, 0.25f, TransitionCurvePreset.Linear);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            // BlendShape 3 本のみ（メタデータは AnimationEvent 側で運搬）
            Assert.AreEqual(3, bindings.Length);

            var byKey = new Dictionary<string, float>();
            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                Assert.AreEqual(typeof(SkinnedMeshRenderer), b.type);
                Assert.IsTrue(b.propertyName.StartsWith("blendShape."),
                    $"Unexpected propertyName: {b.propertyName}");
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                byKey[$"{b.path}|{b.propertyName}"] = curve.Evaluate(0f);
            }

            // 入力エントリは正規化 0..1。AnimationClip カーブは Unity 標準 0..100 スケールで書き込まれる。
            Assert.AreEqual(50f, byKey["Body/Face|blendShape.Smile"], 1e-5f);
            Assert.AreEqual(25f, byKey["Body/Face|blendShape.Anger"], 1e-5f);
            Assert.AreEqual(100f, byKey["Body/Head|blendShape.Surprise"], 1e-5f);
        }

        [Test]
        public void Bake_DoesNotWriteAnimationEvents()
        {
            var clip = CreateTrackedClip();
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.5f),
            };

            ExpressionClipBakery.Bake(clip, entries, 0.7f, TransitionCurvePreset.EaseInOut);

            var events = AnimationUtility.GetAnimationEvents(clip);
            Assert.IsNotNull(events);
            Assert.AreEqual(0, events.Length);
        }

        [Test]
        public void LoadExistingClip_RestoresSliderValues()
        {
            var clip = CreateTrackedClip();
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.5f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Anger", 0.25f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Head", "Surprise", 1.0f),
            };
            ExpressionClipBakery.Bake(clip, entries, 0.25f, TransitionCurvePreset.Linear);

            var sampler = new AnimationClipExpressionSampler();
            var loaded = ExpressionClipBakery.LoadBlendShapeValues(clip, sampler);

            Assert.AreEqual(3, loaded.Count);
            Assert.AreEqual(0.5f, loaded[("Body/Face", "Smile")], 1e-5f);
            Assert.AreEqual(0.25f, loaded[("Body/Face", "Anger")], 1e-5f);
            Assert.AreEqual(1.0f, loaded[("Body/Head", "Surprise")], 1e-5f);
        }

        [Test]
        public void Bake_NullClip_Throws()
        {
            var entries = new List<ExpressionClipBakery.BlendShapeBakeEntry>();
            Assert.Throws<ArgumentNullException>(() =>
                ExpressionClipBakery.Bake(null, entries, 0.25f, TransitionCurvePreset.Linear));
        }

        [Test]
        public void Bake_NullEntries_Throws()
        {
            var clip = CreateTrackedClip();
            Assert.Throws<ArgumentNullException>(() =>
                ExpressionClipBakery.Bake(clip, null, 0.25f, TransitionCurvePreset.Linear));
        }

        [Test]
        public void Bake_RebakeOverwritesExistingCurves()
        {
            var clip = CreateTrackedClip();
            // 1 回目のベイク: Smile + Anger
            var first = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Smile", 0.4f),
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Face", "Anger", 0.8f),
            };
            ExpressionClipBakery.Bake(clip, first, 0.25f, TransitionCurvePreset.Linear);

            // 2 回目のベイク: Surprise のみ。旧 Smile / Anger は削除されるべき
            var second = new List<ExpressionClipBakery.BlendShapeBakeEntry>
            {
                new ExpressionClipBakery.BlendShapeBakeEntry("Body/Head", "Surprise", 1.0f),
            };
            ExpressionClipBakery.Bake(clip, second, 0.25f, TransitionCurvePreset.Linear);

            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length, "再ベイクで旧 BlendShape カーブが削除されること");
            Assert.AreEqual("blendShape.Surprise", bindings[0].propertyName);
        }

        [Test]
        public void BuildBlendShapeNameFallback_SameNameDifferentPaths_KeepsLargerMagnitude()
        {
            var values = new Dictionary<(string rendererPath, string blendShapeName), float>
            {
                [("Body/Face", "Smile")] = 0.25f,
                [("Head", "Smile")] = 0.75f,
                [("Head", "Anger")] = 0.5f,
            };

            var fallback = ExpressionClipBakery.BuildBlendShapeNameFallback(values);

            Assert.AreEqual(2, fallback.Count);
            Assert.AreEqual(0.75f, fallback["Smile"], 1e-5f);
            Assert.AreEqual(0.5f, fallback["Anger"], 1e-5f);
        }

        // ====================================================================
        // ヘルパー
        // ====================================================================

        private ExpressionCreatorWindow CreateWindow()
        {
            var window = ScriptableObject.CreateInstance<ExpressionCreatorWindow>();
            _trackedObjects.Add(window);
            return window;
        }

        private AnimationClip CreateTrackedClip()
        {
            var clip = new AnimationClip();
            _trackedObjects.Add(clip);
            return clip;
        }

        /// <summary>
        /// EditorWindow.CreateGUI は Unity が reflection で呼ぶ private メソッドのため、
        /// テストでも同じ経路で呼び出す（準備目的のみ）。
        /// </summary>
        private static void InvokeCreateGUI(ExpressionCreatorWindow window)
        {
            var createGui = typeof(ExpressionCreatorWindow).GetMethod(
                "CreateGUI",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(createGui);

            createGui.Invoke(window, null);
        }

        // 以下の private アクセスはテストの前提状態を作る準備目的のみ。assert は公開 API で行う。
        private static void SetPrivateField(ExpressionCreatorWindow window, string fieldName, object value)
        {
            var field = typeof(ExpressionCreatorWindow).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);

            field.SetValue(window, value);
        }

        private static void InvokePrivateMethod(ExpressionCreatorWindow window, string methodName, params object[] args)
        {
            var method = typeof(ExpressionCreatorWindow).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(method);

            method.Invoke(window, args);
        }

        /// <summary>
        /// BlendShape 付き SkinnedMeshRenderer を子に持つモデルを生成する。
        /// 各タプルは (レンダラー名, BlendShape 名配列)。
        /// </summary>
        private GameObject CreateModelWithBlendShapes(params (string rendererName, string[] shapes)[] renderers)
        {
            var model = new GameObject("BlendShapeModel");
            _trackedObjects.Add(model);

            for (int r = 0; r < renderers.Length; r++)
            {
                var child = new GameObject(renderers[r].rendererName);
                child.transform.SetParent(model.transform);

                var mesh = new Mesh();
                _trackedObjects.Add(mesh);
                mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
                mesh.triangles = new[] { 0, 1, 2 };

                var deltas = new[] { Vector3.up, Vector3.up, Vector3.up };
                for (int s = 0; s < renderers[r].shapes.Length; s++)
                {
                    mesh.AddBlendShapeFrame(renderers[r].shapes[s], 100f, deltas, null, null);
                }

                var smr = child.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = mesh;
            }

            return model;
        }
    }
}
