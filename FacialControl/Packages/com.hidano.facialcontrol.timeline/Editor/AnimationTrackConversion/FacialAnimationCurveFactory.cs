using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion
{
    /// <summary>
    /// 時刻列ごとの評価値から、中間キーを間引いた AnimationCurve を作る（HID-190。HID-410 でコンバータから分離）。
    /// </summary>
    internal static class FacialAnimationCurveFactory
    {
        /// <summary>BlendShape の間引き閾値（AnimationClip 上の 0..100 スケール。ドメイン値で 0.001）。</summary>
        public const float BlendShapeTolerance = 0.1f;

        /// <summary>目ボーン回転の間引き閾値（度）。</summary>
        public const float GazeRotationToleranceDegrees = 0.1f;
        internal static AnimationCurve BuildScalarCurve(SampleTimeline samples, float[] values, float tolerance)
        {
            var metric = new ScalarChannelMetric(samples.Times, values);
            bool[] steps = samples.DetectSteps(metric, tolerance);
            List<ReducedKey> keys = KeyframeReducer.Reduce(steps, values.Length, metric, tolerance);
            return CreateCurve(samples.Times, keys, i => values[i]);
        }

        internal static AnimationCurve[] BuildQuaternionCurves(SampleTimeline samples, float[] components, float toleranceDegrees)
        {
            var metric = new QuaternionChannelMetric(samples.Times, components);
            bool[] steps = samples.DetectSteps(metric, toleranceDegrees);
            List<ReducedKey> keys = KeyframeReducer.Reduce(steps, components.Length / 4, metric, toleranceDegrees);
            var curves = new AnimationCurve[4];
            for (int c = 0; c < 4; c++)
            {
                int component = c;
                curves[c] = CreateCurve(samples.Times, keys, i => components[(i * 4) + component]);
            }

            return curves;
        }

        /// <summary>
        /// 残すキーから AnimationCurve を作る。接線は Linear、<see cref="ReducedKey.HoldUntilNext"/> のキーと次のキーの間は Constant
        /// （接線の値は tangent mode から Unity が計算する）。
        /// </summary>
        internal static AnimationCurve CreateCurve(IReadOnlyList<double> times, List<ReducedKey> keys, Func<int, float> valueAt)
        {
            var frames = new Keyframe[keys.Count];
            for (int k = 0; k < keys.Count; k++)
            {
                frames[k] = new Keyframe((float)times[keys[k].SampleIndex], valueAt(keys[k].SampleIndex));
            }

            var curve = new AnimationCurve(frames);
            for (int k = 0; k < frames.Length; k++)
            {
                bool holdBefore = k > 0 && keys[k - 1].HoldUntilNext;
                AnimationUtility.SetKeyBroken(curve, k, true);
                AnimationUtility.SetKeyLeftTangentMode(curve, k, holdBefore ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(curve, k, keys[k].HoldUntilNext ? AnimationUtility.TangentMode.Constant : AnimationUtility.TangentMode.Linear);
            }

            return curve;
        }
    }
}
