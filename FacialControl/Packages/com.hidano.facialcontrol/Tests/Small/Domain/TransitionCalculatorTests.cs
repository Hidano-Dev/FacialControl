using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class TransitionCalculatorTests
    {
        // --- Linear カーブ ---

        [Test]
        public void Evaluate_Linear_TZero_ReturnsZero()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float result = TransitionCalculator.Evaluate(curve, 0f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void Evaluate_Linear_TOne_ReturnsOne()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float result = TransitionCalculator.Evaluate(curve, 1f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void Evaluate_Linear_THalf_ReturnsHalf()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_Linear_TQuarter_ReturnsQuarter()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float result = TransitionCalculator.Evaluate(curve, 0.25f);

            Assert.AreEqual(0.25f, result, 0.0001f);
        }

        // --- EaseIn カーブ ---

        [Test]
        public void Evaluate_EaseIn_TZero_ReturnsZero()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float result = TransitionCalculator.Evaluate(curve, 0f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void Evaluate_EaseIn_TOne_ReturnsOne()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float result = TransitionCalculator.Evaluate(curve, 1f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void Evaluate_EaseIn_THalf_LessThanHalf()
        {
            // EaseIn は開始が緩やかなので、t=0.5 の時点では線形より値が小さい
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.Less(result, 0.5f);
            Assert.Greater(result, 0f);
        }

        [Test]
        public void Evaluate_EaseIn_TNearOne_GreaterThanLinear()
        {
            // EaseIn は終盤で加速するため、t=0.9 では線形 0.9 に近いかそれ以上
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float result = TransitionCalculator.Evaluate(curve, 0.9f);

            Assert.Greater(result, 0.5f);
        }

        // --- EaseOut カーブ ---

        [Test]
        public void Evaluate_EaseOut_TZero_ReturnsZero()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);

            float result = TransitionCalculator.Evaluate(curve, 0f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void Evaluate_EaseOut_TOne_ReturnsOne()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);

            float result = TransitionCalculator.Evaluate(curve, 1f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void Evaluate_EaseOut_THalf_GreaterThanHalf()
        {
            // EaseOut は終了が緩やかなので、t=0.5 の時点では線形より値が大きい
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.Greater(result, 0.5f);
            Assert.Less(result, 1f);
        }

        [Test]
        public void Evaluate_EaseOut_TNearZero_GreaterThanLinear()
        {
            // EaseOut は序盤で速いため、t=0.1 では線形 0.1 より大きい
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);

            float result = TransitionCalculator.Evaluate(curve, 0.1f);

            Assert.Greater(result, 0.1f);
        }

        // --- EaseInOut カーブ ---

        [Test]
        public void Evaluate_EaseInOut_TZero_ReturnsZero()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            float result = TransitionCalculator.Evaluate(curve, 0f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void Evaluate_EaseInOut_TOne_ReturnsOne()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            float result = TransitionCalculator.Evaluate(curve, 1f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void Evaluate_EaseInOut_THalf_ReturnsHalf()
        {
            // EaseInOut は t=0.5 で正確に 0.5 を返す（対称カーブ）
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_EaseInOut_TQuarter_LessThanQuarter()
        {
            // EaseInOut の前半は EaseIn と同様に緩やか
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            float result = TransitionCalculator.Evaluate(curve, 0.25f);

            Assert.Less(result, 0.25f);
            Assert.Greater(result, 0f);
        }

        [Test]
        public void Evaluate_EaseInOut_TThreeQuarter_GreaterThanThreeQuarter()
        {
            // EaseInOut の後半は EaseOut と同様に緩やか
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            float result = TransitionCalculator.Evaluate(curve, 0.75f);

            Assert.Greater(result, 0.75f);
            Assert.Less(result, 1f);
        }

        // --- Custom カーブ ---

        [Test]
        public void Evaluate_Custom_LinearKeyframes_ReturnsLinearValue()
        {
            // 線形カーブを手動で定義（キーフレーム 0→0, 1→1, タンジェント 1）
            var keys = new[]
            {
                new CurveKeyFrame(0f, 0f, 1f, 1f),
                new CurveKeyFrame(1f, 1f, 1f, 1f)
            };
            var curve = new TransitionCurve(TransitionCurveType.Custom, keys);

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.AreEqual(0.5f, result, 0.01f);
        }

        [Test]
        public void Evaluate_Custom_TZero_ReturnsFirstKeyValue()
        {
            var keys = new[]
            {
                new CurveKeyFrame(0f, 0.2f, 0f, 0f),
                new CurveKeyFrame(1f, 0.8f, 0f, 0f)
            };
            var curve = new TransitionCurve(TransitionCurveType.Custom, keys);

            float result = TransitionCalculator.Evaluate(curve, 0f);

            Assert.AreEqual(0.2f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_Custom_TOne_ReturnsLastKeyValue()
        {
            var keys = new[]
            {
                new CurveKeyFrame(0f, 0.2f, 0f, 0f),
                new CurveKeyFrame(1f, 0.8f, 0f, 0f)
            };
            var curve = new TransitionCurve(TransitionCurveType.Custom, keys);

            float result = TransitionCalculator.Evaluate(curve, 1f);

            Assert.AreEqual(0.8f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_Custom_EmptyKeys_ReturnsT()
        {
            // キーフレーム空の場合はフォールバックとして Linear と同じ動作
            var curve = new TransitionCurve(TransitionCurveType.Custom, Array.Empty<CurveKeyFrame>());

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_Custom_SingleKey_ReturnsKeyValue()
        {
            // キーフレームが1つの場合は常にその値
            var keys = new[]
            {
                new CurveKeyFrame(0f, 0.7f, 0f, 0f)
            };
            var curve = new TransitionCurve(TransitionCurveType.Custom, keys);

            float result = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.AreEqual(0.7f, result, 0.0001f);
        }

        [Test]
        public void Evaluate_Custom_ThreeKeyframes_InterpolatesCorrectly()
        {
            // 3 キーフレーム: 0→0, 0.5→1, 1→0 (山型カーブ、タンジェント0で平坦)
            var keys = new[]
            {
                new CurveKeyFrame(0f, 0f, 0f, 0f),
                new CurveKeyFrame(0.5f, 1f, 0f, 0f),
                new CurveKeyFrame(1f, 0f, 0f, 0f)
            };
            var curve = new TransitionCurve(TransitionCurveType.Custom, keys);

            float atHalf = TransitionCalculator.Evaluate(curve, 0.5f);

            Assert.AreEqual(1f, atHalf, 0.0001f);
        }

        // --- t の境界値・クランプ ---

        [Test]
        public void Evaluate_TNegative_ClampedToZero()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float result = TransitionCalculator.Evaluate(curve, -0.5f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void Evaluate_TAboveOne_ClampedToOne()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float result = TransitionCalculator.Evaluate(curve, 1.5f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void Evaluate_EaseIn_TNegative_ClampedToZero()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float result = TransitionCalculator.Evaluate(curve, -1f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void Evaluate_EaseOut_TAboveOne_ClampedToOne()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);

            float result = TransitionCalculator.Evaluate(curve, 2f);

            Assert.AreEqual(1f, result);
        }

        // --- ComputeProgress (遷移進行度計算) ---

        [Test]
        public void ComputeProgress_ZeroDuration_ReturnsOne()
        {
            // 遷移時間 0 の場合は即座に切り替え（t=1）
            float result = TransitionCalculator.ComputeProgress(0f, 0f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void ComputeProgress_ZeroDuration_AnyElapsed_ReturnsOne()
        {
            float result = TransitionCalculator.ComputeProgress(0.5f, 0f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void ComputeProgress_HalfElapsed_ReturnsHalf()
        {
            float result = TransitionCalculator.ComputeProgress(0.125f, 0.25f);

            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void ComputeProgress_FullyElapsed_ReturnsOne()
        {
            float result = TransitionCalculator.ComputeProgress(0.25f, 0.25f);

            Assert.AreEqual(1f, result, 0.0001f);
        }

        [Test]
        public void ComputeProgress_OverElapsed_ClampedToOne()
        {
            float result = TransitionCalculator.ComputeProgress(0.5f, 0.25f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void ComputeProgress_ZeroElapsed_ReturnsZero()
        {
            float result = TransitionCalculator.ComputeProgress(0f, 0.25f);

            Assert.AreEqual(0f, result);
        }

        [Test]
        public void ComputeProgress_NegativeElapsed_ClampedToZero()
        {
            float result = TransitionCalculator.ComputeProgress(-0.1f, 0.25f);

            Assert.AreEqual(0f, result);
        }

        // --- ComputeBlendWeight (カーブ＋進行度からブレンドウェイト計算) ---

        [Test]
        public void ComputeBlendWeight_LinearHalf_ReturnsHalf()
        {
            var curve = TransitionCurve.Linear;

            float result = TransitionCalculator.ComputeBlendWeight(curve, 0.125f, 0.25f);

            Assert.AreEqual(0.5f, result, 0.0001f);
        }

        [Test]
        public void ComputeBlendWeight_ZeroDuration_ReturnsOne()
        {
            var curve = TransitionCurve.Linear;

            float result = TransitionCalculator.ComputeBlendWeight(curve, 0f, 0f);

            Assert.AreEqual(1f, result);
        }

        [Test]
        public void ComputeBlendWeight_EaseIn_THalf_LessThanHalf()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float result = TransitionCalculator.ComputeBlendWeight(curve, 0.125f, 0.25f);

            Assert.Less(result, 0.5f);
            Assert.Greater(result, 0f);
        }

        // --- 対称性テスト ---

        [Test]
        public void Evaluate_EaseIn_And_EaseOut_Symmetric()
        {
            // EaseIn(t) + EaseOut(1-t) = 1 を確認（対称カーブ）
            var easeIn = new TransitionCurve(TransitionCurveType.EaseIn);
            var easeOut = new TransitionCurve(TransitionCurveType.EaseOut);

            float easeInVal = TransitionCalculator.Evaluate(easeIn, 0.3f);
            float easeOutVal = TransitionCalculator.Evaluate(easeOut, 0.7f);

            Assert.AreEqual(1f, easeInVal + easeOutVal, 0.0001f);
        }

        // --- 単調増加テスト ---

        [Test]
        public void Evaluate_Linear_IsMonotonicallyIncreasing()
        {
            var curve = new TransitionCurve(TransitionCurveType.Linear);

            float prev = 0f;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float val = TransitionCalculator.Evaluate(curve, t);
                Assert.GreaterOrEqual(val, prev);
                prev = val;
            }
        }

        [Test]
        public void Evaluate_EaseIn_IsMonotonicallyIncreasing()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseIn);

            float prev = 0f;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float val = TransitionCalculator.Evaluate(curve, t);
                Assert.GreaterOrEqual(val, prev);
                prev = val;
            }
        }

        [Test]
        public void Evaluate_EaseOut_IsMonotonicallyIncreasing()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseOut);

            float prev = 0f;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float val = TransitionCalculator.Evaluate(curve, t);
                Assert.GreaterOrEqual(val, prev);
                prev = val;
            }
        }

        [Test]
        public void Evaluate_EaseInOut_IsMonotonicallyIncreasing()
        {
            var curve = new TransitionCurve(TransitionCurveType.EaseInOut);

            float prev = 0f;
            for (int i = 1; i <= 10; i++)
            {
                float t = i / 10f;
                float val = TransitionCalculator.Evaluate(curve, t);
                Assert.GreaterOrEqual(val, prev);
                prev = val;
            }
        }
    }
}
