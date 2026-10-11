using System.Collections.Generic;
using Hidano.FacialControl.Testing;
using Hidano.FacialControl.Timeline.Editor.AnimationTrackConversion;
using NUnit.Framework;
using UnityEngine;

namespace Hidano.FacialControl.Timeline.Tests.EditMode
{
    /// <summary>
    /// AnimationTrack 変換の間引き（<see cref="KeyframeReducer"/>）と時刻列（<see cref="SampleTimeline"/>）の契約（HID-190）。
    /// </summary>
    [SmallTest]
    public sealed class KeyframeReducerTests : SizedTestFixture
    {
        private const float Tolerance = 0.1f;

        [Test]
        public void Reduce_ConstantValues_KeepsOnlyBothEnds()
        {
            float[] values = { 5f, 5f, 5f, 5f, 5f, 5f };

            List<ReducedKey> keys = ReduceScalar(values, null);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 5 }));
            Assert.That(keys[0].HoldUntilNext, Is.False);
        }

        [Test]
        public void Reduce_ConstantSlope_KeepsOnlyBothEnds()
        {
            float[] values = { 0f, 10f, 20f, 30f, 40f, 50f };

            List<ReducedKey> keys = ReduceScalar(values, null);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 5 }));
        }

        [Test]
        public void Reduce_RampThenFlat_KeepsCorner()
        {
            float[] values = { 0f, 25f, 50f, 75f, 100f, 100f, 100f, 100f };

            List<ReducedKey> keys = ReduceScalar(values, null);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 4, 7 }));
        }

        [Test]
        public void Reduce_DeviationWithinTolerance_RemovesMiddleKey()
        {
            float[] values = { 0f, 0.05f, -0.05f, 0.09f, 0f };

            List<ReducedKey> keys = ReduceScalar(values, null);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 4 }));
        }

        [Test]
        public void Reduce_DeviationAboveTolerance_KeepsMiddleKey()
        {
            float[] values = { 0f, 0f, 0.2f, 0f, 0f };

            List<ReducedKey> keys = ReduceScalar(values, null);

            Assert.That(Indices(keys), Does.Contain(2));
        }

        [Test]
        public void Reduce_StepAfterFlat_HoldsFromLastKeptKeyAndDropsProbeKey()
        {
            // index 3 は段差の直前（値 0）、index 4 で 100 へ跳ぶ。
            float[] values = { 0f, 0f, 0f, 0f, 100f, 100f };
            bool[] steps = { false, false, false, false, true, false };

            List<ReducedKey> keys = ReduceScalar(values, steps);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 4, 5 }));
            Assert.That(keys[0].HoldUntilNext, Is.True, "段差の前の区間は保持（Constant）");
            Assert.That(keys[1].HoldUntilNext, Is.False);
        }

        [Test]
        public void Reduce_StepAfterRamp_HoldsAtProbeKey()
        {
            float[] values = { 0f, 10f, 20f, 30f, 80f, 80f };
            bool[] steps = { false, false, false, false, true, false };

            List<ReducedKey> keys = ReduceScalar(values, steps);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 3, 4, 5 }));
            Assert.That(keys[1].HoldUntilNext, Is.True);
            Assert.That(keys[0].HoldUntilNext, Is.False);
        }

        [Test]
        public void Reduce_QuaternionConstantAngularSpeedSmallArc_KeepsOnlyBothEnds()
        {
            var components = new List<float>();
            for (int i = 0; i <= 10; i++)
            {
                AddQuaternion(components, Quaternion.Euler(0f, i * 1f, 0f));
            }

            double[] times = Times(11);
            var metric = new QuaternionChannelMetric(times, components);
            List<ReducedKey> keys = KeyframeReducer.Reduce(null, 11, metric, Tolerance);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 10 }));
        }

        [Test]
        public void Reduce_QuaternionTurnsBack_KeepsTurningPoint()
        {
            var components = new List<float>();
            float[] yaw = { 0f, 5f, 10f, 5f, 0f };
            for (int i = 0; i < yaw.Length; i++)
            {
                AddQuaternion(components, Quaternion.Euler(0f, yaw[i], 0f));
            }

            var metric = new QuaternionChannelMetric(Times(yaw.Length), components);
            List<ReducedKey> keys = KeyframeReducer.Reduce(null, yaw.Length, metric, Tolerance);

            Assert.That(Indices(keys), Is.EqualTo(new[] { 0, 2, 4 }));
        }

        [Test]
        public void AlignHemispheres_NegatedNeighbor_FlipsSign()
        {
            Quaternion q = Quaternion.Euler(0f, 10f, 0f);
            var components = new List<float>();
            AddQuaternion(components, q);
            AddQuaternion(components, new Quaternion(-q.x, -q.y, -q.z, -q.w));

            QuaternionChannelMetric.AlignHemispheres(components);

            Assert.That(components[7], Is.EqualTo(q.w).Within(1e-6f));
        }

        [Test]
        public void CreateCurve_LinearAndHoldKeys_EvaluatesAsLinearThenStep()
        {
            double[] times = { 0d, 1d, 2d };
            float[] values = { 0f, 10f, 50f };
            var keys = new List<ReducedKey> { new ReducedKey(0, false), new ReducedKey(1, true), new ReducedKey(2, false) };

            AnimationCurve curve = FacialAnimationTrackConverter.CreateCurve(times, keys, i => values[i]);

            Assert.That(curve.Evaluate(0.5f), Is.EqualTo(5f).Within(1e-4f), "Linear");
            Assert.That(curve.Evaluate(1.99f), Is.EqualTo(10f).Within(1e-4f), "Constant（次のキーまで保持）");
            Assert.That(curve.Evaluate(2f), Is.EqualTo(50f).Within(1e-4f), "次のキーで段差");
        }

        [Test]
        public void SampleTimeline_Create_AddsFramesAndProbePairsInOrder()
        {
            SampleTimeline samples = SampleTimeline.Create(0.1d, 20d, new[] { 0.025d });

            Assert.That(samples.Times.Count, Is.EqualTo(5), "フレーム 0 / 0.05 / 0.1 に段差候補の (t - probe, t) を加える");
            Assert.That(samples.Times[0], Is.EqualTo(0d));
            Assert.That(samples.Times[1], Is.EqualTo(0.025d - FacialAnimationTrackConverter.StepProbeSeconds).Within(1e-9d));
            Assert.That(samples.Times[2], Is.EqualTo(0.025d).Within(1e-9d));
            Assert.That(samples.Times[4], Is.EqualTo(0.1d).Within(1e-9d));
        }

        [Test]
        public void SampleTimeline_DetectSteps_FlagsOnlyJumpsAboveTolerance()
        {
            SampleTimeline samples = SampleTimeline.Create(0.1d, 20d, new[] { 0.025d, 0.075d });

            // 0 / 0.0245 / 0.025 / 0.05 / 0.0745 / 0.075 / 0.1
            float[] values = { 0f, 0f, 100f, 100f, 100f, 100.05f, 100.05f };
            var metric = new ScalarChannelMetric(samples.Times, values);
            bool[] steps = samples.DetectSteps(metric, Tolerance);

            Assert.That(steps[2], Is.True, "0.025 で跳ぶ");
            Assert.That(steps[5], Is.False, "閾値以下の差は段差にしない");
        }

        [Test]
        public void SanitizeFileName_InvalidCharacters_ReplacedWithUnderscore()
        {
            Assert.That(FacialAnimationTrackConverter.SanitizeFileName("Take:1/顔?"), Is.EqualTo("Take_1_顔_"));
        }

        private static List<ReducedKey> ReduceScalar(float[] values, bool[] steps)
        {
            var metric = new ScalarChannelMetric(Times(values.Length), values);
            return KeyframeReducer.Reduce(steps, values.Length, metric, Tolerance);
        }

        private static double[] Times(int count)
        {
            var times = new double[count];
            for (int i = 0; i < count; i++)
            {
                times[i] = i / 60d;
            }

            return times;
        }

        private static int[] Indices(List<ReducedKey> keys)
        {
            var indices = new int[keys.Count];
            for (int i = 0; i < keys.Count; i++)
            {
                indices[i] = keys[i].SampleIndex;
            }

            return indices;
        }

        private static void AddQuaternion(List<float> components, Quaternion q)
        {
            components.Add(q.x);
            components.Add(q.y);
            components.Add(q.z);
            components.Add(q.w);
        }
    }
}
