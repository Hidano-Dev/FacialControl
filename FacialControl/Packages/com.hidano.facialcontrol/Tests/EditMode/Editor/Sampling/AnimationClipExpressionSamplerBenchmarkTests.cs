using System;
using System.Collections.Generic;
using System.Diagnostics;
using Hidano.FacialControl.Editor.Sampling;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Editor.Sampling
{
    /// <summary>
    /// <see cref="AnimationClipExpressionSampler"/> のパフォーマンス保証。
    /// 1 Expression あたり 50ms 以内（典型 ~10 BlendShapes）でサンプリング完了することを確認する。
    /// </summary>
    [TestFixture]
    [MediumTest]
    public class AnimationClipExpressionSamplerBenchmarkTests : SizedTestFixture
    {
        private const int TypicalBlendShapeCount = 10;
        private const int WarmupIterations = 3;
        private const int MeasureIterations = 30;
        private const double BudgetMilliseconds = 50.0;

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

        [Test]
        public void SampleSnapshot_TenBlendShapes_CompletesWithinFiftyMilliseconds()
        {
            var clip = CreateTypicalClip(TypicalBlendShapeCount);
            var sampler = new AnimationClipExpressionSampler();

            // ウォームアップ（JIT・AssetDatabase キャッシュの安定化）
            for (int i = 0; i < WarmupIterations; i++)
            {
                sampler.SampleSnapshot("expr-warmup", clip);
            }

            double minMilliseconds = double.PositiveInfinity;
            double maxMilliseconds = 0.0;
            double totalMilliseconds = 0.0;

            var stopwatch = new Stopwatch();
            for (int i = 0; i < MeasureIterations; i++)
            {
                stopwatch.Restart();
                sampler.SampleSnapshot("expr-bench", clip);
                stopwatch.Stop();

                double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
                if (elapsedMs < minMilliseconds) minMilliseconds = elapsedMs;
                if (elapsedMs > maxMilliseconds) maxMilliseconds = elapsedMs;
                totalMilliseconds += elapsedMs;
            }

            double avgMilliseconds = totalMilliseconds / MeasureIterations;
            UnityEngine.Debug.Log(
                $"[AnimationClipExpressionSamplerBenchmark] BlendShapes={TypicalBlendShapeCount} " +
                $"min={minMilliseconds:F3}ms avg={avgMilliseconds:F3}ms max={maxMilliseconds:F3}ms " +
                $"(budget={BudgetMilliseconds:F1}ms, iterations={MeasureIterations})");

            // 平均値で判定（個別の OS スパイクで稀に max が膨らむため）
            Assert.Less(avgMilliseconds, BudgetMilliseconds,
                $"SampleSnapshot average {avgMilliseconds:F3}ms exceeded budget {BudgetMilliseconds:F1}ms " +
                $"(min={minMilliseconds:F3}ms, max={maxMilliseconds:F3}ms).");
        }

        [Test]
        public void SampleSummary_TenBlendShapes_CompletesWithinFiftyMilliseconds()
        {
            var clip = CreateTypicalClip(TypicalBlendShapeCount);
            var sampler = new AnimationClipExpressionSampler();

            for (int i = 0; i < WarmupIterations; i++)
            {
                sampler.SampleSummary(clip);
            }

            double totalMilliseconds = 0.0;
            var stopwatch = new Stopwatch();
            for (int i = 0; i < MeasureIterations; i++)
            {
                stopwatch.Restart();
                sampler.SampleSummary(clip);
                stopwatch.Stop();
                totalMilliseconds += stopwatch.Elapsed.TotalMilliseconds;
            }

            double avgMilliseconds = totalMilliseconds / MeasureIterations;
            UnityEngine.Debug.Log(
                $"[AnimationClipExpressionSamplerBenchmark] (Summary) avg={avgMilliseconds:F3}ms " +
                $"(budget={BudgetMilliseconds:F1}ms, iterations={MeasureIterations})");

            Assert.Less(avgMilliseconds, BudgetMilliseconds,
                $"SampleSummary average {avgMilliseconds:F3}ms exceeded budget {BudgetMilliseconds:F1}ms.");
        }

        private AnimationClip CreateTypicalClip(int blendShapeCount)
        {
            var clip = new AnimationClip();
            _trackedObjects.Add(clip);

            for (int i = 0; i < blendShapeCount; i++)
            {
                SetFloatCurve(
                    clip,
                    "Body/Face",
                    typeof(SkinnedMeshRenderer),
                    $"blendShape.Shape{i:D2}",
                    i * 0.1f);
            }

            // Bone curves（典型的に 1 ボーン分の position/euler/scale 9 軸を含める）
            string bonePath = "Armature/Head";
            SetFloatCurve(clip, bonePath, typeof(Transform), "m_LocalPosition.x", 0f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "m_LocalPosition.y", 0f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "m_LocalPosition.z", 0f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "localEulerAnglesRaw.x", 0f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "localEulerAnglesRaw.y", 0f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "localEulerAnglesRaw.z", 0f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "m_LocalScale.x", 1f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "m_LocalScale.y", 1f);
            SetFloatCurve(clip, bonePath, typeof(Transform), "m_LocalScale.z", 1f);

            return clip;
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
