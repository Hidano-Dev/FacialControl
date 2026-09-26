using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    /// <summary>
    /// <see cref="AnimationClipExpressionSampler"/> のテスト。
    /// AnimationUtility 経由で時刻 0 の BlendShape / Transform 値を取得し、不明 binding を warning + skip すること、
    /// 遷移メタデータは AnimationEvent を参照せず常に既定値を返すこと、
    /// および TryResolveContributeIndices による ContributeMask 解決を検証する。
    /// </summary>
    [TestFixture]
    public class AnimationClipExpressionSamplerTests
    {
        private readonly List<UnityEngine.Object> _trackedObjects = new List<UnityEngine.Object>();

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
        }

        // ---- SampleSnapshot: カーブ値の取得 ----

        [Test]
        public void SampleSnapshot_BlendShapeCurves_ReturnsValuesAtTimeZero()
        {
            var clip = CreateTrackedClip();
            // blendShape カーブは Unity 標準 0..100 スケール。snapshot へは正規化 0..1 で格納される。
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 50f);
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Anger", 25f);
            SetFloatCurve(clip, "Body/Head", typeof(SkinnedMeshRenderer), "blendShape.Surprise", 100f);

            var sampler = new AnimationClipExpressionSampler();
            var snapshot = sampler.SampleSnapshot("expr-001", clip);

            Assert.AreEqual("expr-001", snapshot.Id);
            Assert.AreEqual(3, snapshot.BlendShapes.Length);

            var byKey = new Dictionary<string, BlendShapeSnapshot>();
            for (int i = 0; i < snapshot.BlendShapes.Length; i++)
            {
                var bs = snapshot.BlendShapes.Span[i];
                byKey[$"{bs.RendererPath}|{bs.Name}"] = bs;
            }
            Assert.AreEqual(0.5f, byKey["Body/Face|Smile"].Value, 1e-5f);
            Assert.AreEqual(0.25f, byKey["Body/Face|Anger"].Value, 1e-5f);
            Assert.AreEqual(1.0f, byKey["Body/Head|Surprise"].Value, 1e-5f);

            // Renderer paths は重複排除されている
            Assert.AreEqual(2, snapshot.RendererPaths.Length);
            var paths = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < snapshot.RendererPaths.Length; i++)
            {
                paths.Add(snapshot.RendererPaths.Span[i]);
            }
            Assert.IsTrue(paths.Contains("Body/Face"));
            Assert.IsTrue(paths.Contains("Body/Head"));

            // metadata 抽出を行わないため fallback default
            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSnapshot_BlendShapePartialWeights_AreNormalizedNotSaturated()
        {
            // 回帰: 手書き / インポートした標準 Unity clip は blendShape を 0..100 で記録する。
            // キーフレーム 30 / 40 が runtime で ×100 され全て 100 に飽和していた不具合の防止。
            // snapshot は正規化 0..1 (0.3 / 0.4) を返し、ドメイン → runtime ×100 で 30 / 40 に戻る。
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.MouthOpen", 30f);
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.BrowDown", 40f);

            var sampler = new AnimationClipExpressionSampler();
            var snapshot = sampler.SampleSnapshot("expr-partial", clip);

            var byKey = new Dictionary<string, float>();
            for (int i = 0; i < snapshot.BlendShapes.Length; i++)
            {
                var bs = snapshot.BlendShapes.Span[i];
                byKey[bs.Name] = bs.Value;
            }

            Assert.AreEqual(0.3f, byKey["MouthOpen"], 1e-5f);
            Assert.AreEqual(0.4f, byKey["BrowDown"], 1e-5f);
        }

        [Test]
        public void SampleSnapshot_TransformCurves_ReturnsBoneSnapshot()
        {
            var clip = CreateTrackedClip();
            // Position
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.x", 0.1f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.y", 0.2f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.z", 0.3f);
            // Euler (raw 形式)
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "localEulerAnglesRaw.x", 10f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "localEulerAnglesRaw.y", 20f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "localEulerAnglesRaw.z", 30f);
            // Scale
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalScale.x", 1.1f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalScale.y", 1.2f);
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalScale.z", 1.3f);

            var sampler = new AnimationClipExpressionSampler();
            var snapshot = sampler.SampleSnapshot("expr-002", clip);

            Assert.AreEqual(1, snapshot.Bones.Length);
            var bone = snapshot.Bones.Span[0];
            Assert.AreEqual("Armature/Head", bone.BonePath);
            Assert.AreEqual(0.1f, bone.PositionX, 1e-5f);
            Assert.AreEqual(0.2f, bone.PositionY, 1e-5f);
            Assert.AreEqual(0.3f, bone.PositionZ, 1e-5f);
            Assert.AreEqual(10f, bone.EulerX, 1e-5f);
            Assert.AreEqual(20f, bone.EulerY, 1e-5f);
            Assert.AreEqual(30f, bone.EulerZ, 1e-5f);
            Assert.AreEqual(1.1f, bone.ScaleX, 1e-5f);
            Assert.AreEqual(1.2f, bone.ScaleY, 1e-5f);
            Assert.AreEqual(1.3f, bone.ScaleZ, 1e-5f);
        }

        [Test]
        public void SampleSnapshot_UnsupportedBinding_LogsWarningAndSkips()
        {
            var clip = CreateTrackedClip();
            // 既知の正常 binding（BlendShape）
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 0.5f);
            // 未知の binding（Renderer.m_Color.r 等は本サンプラのスコープ外）
            SetFloatCurve(clip, "Body/Face", typeof(Renderer), "material._Color.r", 0.7f);

            var sampler = new AnimationClipExpressionSampler();

            LogAssert.Expect(LogType.Warning, new Regex("Unsupported binding"));
            var snapshot = sampler.SampleSnapshot("expr-003", clip);

            // 未対応 binding は skip され、BlendShape のみ採用される
            Assert.AreEqual(1, snapshot.BlendShapes.Length);
            Assert.AreEqual("Smile", snapshot.BlendShapes.Span[0].Name);
            Assert.AreEqual(0, snapshot.Bones.Length);
        }

        [Test]
        public void SampleSnapshot_NullClip_Throws()
        {
            var sampler = new AnimationClipExpressionSampler();

            Assert.Throws<ArgumentNullException>(() =>
                sampler.SampleSnapshot("expr-null", null));
        }

        // ---- SampleSummary ----

        [Test]
        public void SampleSummary_ReturnsRendererPathsAndBlendShapeNames()
        {
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 0.5f);
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Anger", 0.25f);
            SetFloatCurve(clip, "Body/Head", typeof(SkinnedMeshRenderer), "blendShape.Surprise", 1.0f);

            var sampler = new AnimationClipExpressionSampler();
            var summary = sampler.SampleSummary(clip);

            Assert.IsNotNull(summary.RendererPaths);
            Assert.IsNotNull(summary.BlendShapeNames);

            var rendererSet = new HashSet<string>(summary.RendererPaths);
            Assert.AreEqual(2, rendererSet.Count);
            Assert.IsTrue(rendererSet.Contains("Body/Face"));
            Assert.IsTrue(rendererSet.Contains("Body/Head"));

            var blendShapeSet = new HashSet<string>(summary.BlendShapeNames);
            Assert.AreEqual(3, blendShapeSet.Count);
            Assert.IsTrue(blendShapeSet.Contains("Smile"));
            Assert.IsTrue(blendShapeSet.Contains("Anger"));
            Assert.IsTrue(blendShapeSet.Contains("Surprise"));

            Assert.AreEqual(Expression.DefaultTransitionDuration, summary.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, summary.TransitionCurve);
        }

        // ---- 遷移メタデータ: AnimationEvent は参照せず常に既定値を返す ----
        // clip 上に FacialControlMeta_Set の AnimationEvent が残っていても、
        // サンプラはそれを遷移情報の真値として扱わない。

        [Test]
        public void SampleSnapshot_NoMetadata_ReturnsDefaultTransitionMetadata()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-default", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSummary_NoMetadata_ReturnsDefaultTransitionDuration()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            var sampler = new AnimationClipExpressionSampler();

            var summary = sampler.SampleSummary(clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, summary.TransitionDuration);
        }

        [Test]
        public void SampleSummary_NoMetadata_ReturnsLinearCurvePreset()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            var sampler = new AnimationClipExpressionSampler();

            var summary = sampler.SampleSummary(clip);

            Assert.AreEqual(TransitionCurvePreset.Linear, summary.TransitionCurve);
        }

        [Test]
        public void SampleSnapshot_DurationMetaEvent_IgnoresEventAndReturnsDefaultTransitionDuration()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionDuration", 0.5f),
            });

            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-duration", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSnapshot_CurvePresetMetaEvent_IgnoresEventAndReturnsLinearCurvePreset()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionCurvePreset", (float)(int)TransitionCurvePreset.EaseInOut),
            });

            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-curve", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
        }

        [Test]
        public void SampleSummary_MetaEvents_IgnoresEventsAndReturnsDefaultsWithoutError()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionDuration", 0.4f),
                CreateMetaEvent("transitionCurvePreset", (float)(int)TransitionCurvePreset.EaseInOut),
            });

            var sampler = new AnimationClipExpressionSampler();

            var summary = sampler.SampleSummary(clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, summary.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, summary.TransitionCurve);
        }

        [Test]
        public void SampleSnapshot_DuplicateMetaEvents_IgnoresEventsAndReturnsDefaultsWithoutWarning()
        {
            var clip = CreateTrackedClipWithSmileCurve();
            SetMetaEvents(clip, new[]
            {
                CreateMetaEvent("transitionDuration", 0.4f),
                CreateMetaEvent("transitionDuration", 0.7f),
            });

            var sampler = new AnimationClipExpressionSampler();

            var snapshot = sampler.SampleSnapshot("expr-meta-dup", clip);

            Assert.AreEqual(Expression.DefaultTransitionDuration, snapshot.TransitionDuration);
            Assert.AreEqual(TransitionCurvePreset.Linear, snapshot.TransitionCurvePreset);
            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void MetaSetFunctionName_IsExposedAsConstant()
        {
            Assert.AreEqual("FacialControlMeta_Set", AnimationClipExpressionSampler.MetaSetFunctionName);
        }

        // ---- TryResolveContributeIndices: ContributeMask 解決 ----

        [Test]
        public void TryResolveContributeIndices_MultipleBlendShapeCurves_SetsAllMatchingIndices()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "Smile", 0.5f);
            SetBlendShapeConstantCurve(clip, "Body/Face", "MouthOpen", 1.0f);
            SetBlendShapeConstantCurve(clip, "Body/Head", "BlinkLeft", 0.25f);

            var blendShapeNames = new[] { "Smile", "Anger", "BlinkLeft", "MouthOpen" };
            var mask = new BitArray(blendShapeNames.Length);

            bool resolved = InvokeTryResolveContributeIndices(clip, blendShapeNames, mask);

            Assert.IsTrue(resolved);
            AssertMask(mask, 0, 2, 3);
        }

        [Test]
        public void TryResolveContributeIndices_NonBlendShapeCurvesOnly_ReturnsFalseAndLeavesEmptyMask()
        {
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Armature/Head", typeof(Transform), "m_LocalPosition.x", 0.5f);
            SetFloatCurve(clip, "Body/Face", typeof(Renderer), "material._Color.r", 0.8f);

            var blendShapeNames = new[] { "Smile", "BlinkLeft", "MouthOpen" };
            var mask = new BitArray(blendShapeNames.Length);

            bool resolved = InvokeTryResolveContributeIndices(clip, blendShapeNames, mask);

            Assert.IsFalse(resolved);
            AssertMask(mask);
        }

        [Test]
        public void TryResolveContributeIndices_MultibyteAndSymbolBlendShapeNames_UsesExactNames()
        {
            var clip = CreateTrackedClip();
            SetBlendShapeConstantCurve(clip, "Body/Face", "怒り眉★左", 0.75f);
            SetBlendShapeConstantCurve(clip, "Body/Face", "口_A+B(右)", 0.4f);

            var blendShapeNames = new[] { "怒り眉★左", "口_A+B(右)", "怒り眉★右" };
            var mask = new BitArray(blendShapeNames.Length);

            bool resolved = InvokeTryResolveContributeIndices(clip, blendShapeNames, mask);

            Assert.IsTrue(resolved);
            AssertMask(mask, 0, 1);
        }

        // ---- ヘルパー ----

        /// <summary>カーブ未設定の空 clip を生成し、TearDown で破棄されるよう追跡する。</summary>
        private AnimationClip CreateTrackedClip()
        {
            var clip = new AnimationClip();
            _trackedObjects.Add(clip);
            return clip;
        }

        /// <summary>BlendShape カーブ 1 本（Body/Face の Smile）を持つ clip を生成し追跡する。遷移メタデータ系テスト用。</summary>
        private AnimationClip CreateTrackedClipWithSmileCurve()
        {
            var clip = CreateTrackedClip();
            SetFloatCurve(clip, "Body/Face", typeof(SkinnedMeshRenderer), "blendShape.Smile", 0.5f);
            return clip;
        }

        private static AnimationEvent CreateMetaEvent(string key, float value)
        {
            return new AnimationEvent
            {
                time = 0f,
                functionName = AnimationClipExpressionSampler.MetaSetFunctionName,
                stringParameter = key,
                floatParameter = value,
            };
        }

        private static void SetMetaEvents(AnimationClip clip, AnimationEvent[] events)
        {
            AnimationUtility.SetAnimationEvents(clip, events);
        }

        private static bool InvokeTryResolveContributeIndices(
            AnimationClip clip,
            IReadOnlyList<string> blendShapeNames,
            BitArray output)
        {
            var sampler = new AnimationClipExpressionSampler();
            var method = typeof(AnimationClipExpressionSampler).GetMethod(
                "TryResolveContributeIndices",
                BindingFlags.Instance | BindingFlags.Public,
                null,
                new[] { typeof(AnimationClip), typeof(IReadOnlyList<string>), typeof(BitArray) },
                null);

            Assert.IsNotNull(
                method,
                "AnimationClipExpressionSampler must expose TryResolveContributeIndices(AnimationClip, IReadOnlyList<string>, BitArray).");

            return (bool)method.Invoke(sampler, new object[] { clip, blendShapeNames, output });
        }

        private static void AssertMask(BitArray mask, params int[] expectedTrueIndices)
        {
            var expected = new HashSet<int>(expectedTrueIndices);
            for (int i = 0; i < mask.Length; i++)
            {
                Assert.AreEqual(expected.Contains(i), mask[i], $"mask[{i}]");
            }
        }

        private static void SetBlendShapeConstantCurve(
            AnimationClip clip,
            string path,
            string blendShapeName,
            float value)
        {
            SetFloatCurve(clip, path, typeof(SkinnedMeshRenderer), "blendShape." + blendShapeName, value);
        }

        private static void SetFloatCurve(AnimationClip clip, string path, Type type, string propertyName, float value)
        {
            var binding = new EditorCurveBinding
            {
                path = path,
                type = type,
                propertyName = propertyName,
            };
            var curve = AnimationCurve.Constant(0f, 1f, value);
            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }
    }
}
