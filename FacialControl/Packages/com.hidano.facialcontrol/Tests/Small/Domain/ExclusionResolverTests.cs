using System;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    [TestFixture]
    public class ExclusionResolverTests
    {
        // --- LastWins クロスフェード ---

        [Test]
        public void ResolveLastWins_WeightZero_ReturnsFromValues()
        {
            // weight=0 → 遷移開始時点、from の値がそのまま返る
            var fromValues = new float[] { 0.8f, 0.5f };
            var toValues = new float[] { 0.2f, 1.0f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 0f, output);

            Assert.AreEqual(0.8f, output[0], 0.0001f);
            Assert.AreEqual(0.5f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_WeightOne_ReturnsToValues()
        {
            // weight=1 → 遷移完了、to の値がそのまま返る
            var fromValues = new float[] { 0.8f, 0.5f };
            var toValues = new float[] { 0.2f, 1.0f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 1f, output);

            Assert.AreEqual(0.2f, output[0], 0.0001f);
            Assert.AreEqual(1.0f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_WeightHalf_ReturnsInterpolatedValues()
        {
            // weight=0.5 → from と to の中間値
            var fromValues = new float[] { 0.0f, 1.0f };
            var toValues = new float[] { 1.0f, 0.0f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 0.5f, output);

            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.5f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_WeightQuarter_ReturnsCorrectInterpolation()
        {
            // weight=0.25 → from寄りの補間値
            var fromValues = new float[] { 0.0f, 0.8f };
            var toValues = new float[] { 1.0f, 0.0f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 0.25f, output);

            // lerp(0.0, 1.0, 0.25) = 0.25
            Assert.AreEqual(0.25f, output[0], 0.0001f);
            // lerp(0.8, 0.0, 0.25) = 0.6
            Assert.AreEqual(0.6f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_ResultClampedToZeroOne()
        {
            // 補間結果は 0〜1 にクランプされる
            var fromValues = new float[] { 1.0f };
            var toValues = new float[] { 1.0f };
            var output = new float[1];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 0.5f, output);

            Assert.GreaterOrEqual(output[0], 0f);
            Assert.LessOrEqual(output[0], 1f);
        }

        [Test]
        public void ResolveLastWins_EmptyArrays_NoException()
        {
            // 空配列でも例外なく動作
            var fromValues = Array.Empty<float>();
            var toValues = Array.Empty<float>();
            var output = Array.Empty<float>();

            Assert.DoesNotThrow(() =>
                ExclusionResolver.ResolveLastWins(fromValues, toValues, 0.5f, output));
        }

        [Test]
        public void ResolveLastWins_WeightClampedBelowZero_TreatedAsZero()
        {
            // 負のウェイトは 0 にクランプ
            var fromValues = new float[] { 0.8f };
            var toValues = new float[] { 0.2f };
            var output = new float[1];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, -0.5f, output);

            Assert.AreEqual(0.8f, output[0], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_WeightClampedAboveOne_TreatedAsOne()
        {
            // 1 を超えるウェイトは 1 にクランプ
            var fromValues = new float[] { 0.8f };
            var toValues = new float[] { 0.2f };
            var output = new float[1];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 1.5f, output);

            Assert.AreEqual(0.2f, output[0], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_SingleElement_CorrectInterpolation()
        {
            // 要素数1の配列で正しく補間
            var fromValues = new float[] { 0.3f };
            var toValues = new float[] { 0.9f };
            var output = new float[1];

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 0.5f, output);

            Assert.AreEqual(0.6f, output[0], 0.0001f);
        }

        [Test]
        public void ResolveLastWins_LargeArray_CorrectInterpolation()
        {
            // 大きい配列でも全要素正しく補間
            int size = 100;
            var fromValues = new float[size];
            var toValues = new float[size];
            var output = new float[size];

            for (int i = 0; i < size; i++)
            {
                fromValues[i] = 0f;
                toValues[i] = 1f;
            }

            ExclusionResolver.ResolveLastWins(fromValues, toValues, 0.75f, output);

            for (int i = 0; i < size; i++)
            {
                Assert.AreEqual(0.75f, output[i], 0.0001f);
            }
        }

        // --- Span オーバーロード（LastWins） ---

        [Test]
        public void ResolveLastWins_Span_WeightHalf_ReturnsInterpolatedValues()
        {
            // Span ベースのオーバーロードでも正しく動作
            var fromValues = new float[] { 0.0f, 1.0f };
            var toValues = new float[] { 1.0f, 0.0f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(
                new ReadOnlySpan<float>(fromValues),
                new ReadOnlySpan<float>(toValues),
                0.5f,
                new Span<float>(output));

            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.5f, output[1], 0.0001f);
        }

        // --- Blend 加算+クランプ ---

        [Test]
        public void ResolveBlend_SingleExpression_ReturnsWeightedValues()
        {
            // 単一 Expression の場合、weight を乗算した値が返る
            var values = new float[] { 0.8f, 0.5f };
            var output = new float[2];
            // output は事前にゼロ初期化されている前提

            ExclusionResolver.ResolveBlend(values, 1.0f, output);

            Assert.AreEqual(0.8f, output[0], 0.0001f);
            Assert.AreEqual(0.5f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveBlend_HalfWeight_ReturnsHalfValues()
        {
            // weight=0.5 でスケーリング
            var values = new float[] { 1.0f, 0.6f };
            var output = new float[2];

            ExclusionResolver.ResolveBlend(values, 0.5f, output);

            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.3f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveBlend_Accumulation_AddsToExistingValues()
        {
            // 既存の値に加算される（Blend モードの複数 Expression 積み重ね）
            var values = new float[] { 0.4f, 0.3f };
            var output = new float[] { 0.3f, 0.5f };

            ExclusionResolver.ResolveBlend(values, 1.0f, output);

            Assert.AreEqual(0.7f, output[0], 0.0001f);
            Assert.AreEqual(0.8f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveBlend_AccumulationExceedsOne_ClampedToOne()
        {
            // 加算結果が 1 を超える場合はクランプ
            var values = new float[] { 0.8f, 0.9f };
            var output = new float[] { 0.5f, 0.5f };

            ExclusionResolver.ResolveBlend(values, 1.0f, output);

            Assert.AreEqual(1.0f, output[0], 0.0001f);
            Assert.AreEqual(1.0f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveBlend_ZeroWeight_NoChange()
        {
            // weight=0 の場合は何も加算されない
            var values = new float[] { 1.0f, 1.0f };
            var output = new float[] { 0.5f, 0.3f };

            ExclusionResolver.ResolveBlend(values, 0f, output);

            Assert.AreEqual(0.5f, output[0], 0.0001f);
            Assert.AreEqual(0.3f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveBlend_MultipleExpressions_CorrectAccumulation()
        {
            // 複数の Expression を順番に加算
            var expr1 = new float[] { 0.3f, 0.4f, 0.5f };
            var expr2 = new float[] { 0.4f, 0.3f, 0.2f };
            var output = new float[3];

            ExclusionResolver.ResolveBlend(expr1, 1.0f, output);
            ExclusionResolver.ResolveBlend(expr2, 1.0f, output);

            Assert.AreEqual(0.7f, output[0], 0.0001f);
            Assert.AreEqual(0.7f, output[1], 0.0001f);
            Assert.AreEqual(0.7f, output[2], 0.0001f);
        }

        [Test]
        public void ResolveBlend_MultipleExpressionsWithWeight_CorrectAccumulation()
        {
            // weight 付きの加算
            var expr1 = new float[] { 0.6f, 0.8f };
            var expr2 = new float[] { 0.4f, 0.6f };
            var output = new float[2];

            ExclusionResolver.ResolveBlend(expr1, 0.5f, output);
            ExclusionResolver.ResolveBlend(expr2, 0.5f, output);

            // 0.6*0.5 + 0.4*0.5 = 0.3 + 0.2 = 0.5
            Assert.AreEqual(0.5f, output[0], 0.0001f);
            // 0.8*0.5 + 0.6*0.5 = 0.4 + 0.3 = 0.7
            Assert.AreEqual(0.7f, output[1], 0.0001f);
        }

        [Test]
        public void ResolveBlend_EmptyArrays_NoException()
        {
            var values = Array.Empty<float>();
            var output = Array.Empty<float>();

            Assert.DoesNotThrow(() => ExclusionResolver.ResolveBlend(values, 1.0f, output));
        }

        [Test]
        public void ResolveBlend_WeightClampedBelowZero_TreatedAsZero()
        {
            // 負のウェイトは 0 にクランプ
            var values = new float[] { 1.0f };
            var output = new float[] { 0.3f };

            ExclusionResolver.ResolveBlend(values, -0.5f, output);

            Assert.AreEqual(0.3f, output[0], 0.0001f);
        }

        [Test]
        public void ResolveBlend_WeightClampedAboveOne_TreatedAsOne()
        {
            // 1 を超えるウェイトは 1 にクランプ
            var values = new float[] { 0.5f };
            var output = new float[] { 0.0f };

            ExclusionResolver.ResolveBlend(values, 1.5f, output);

            Assert.AreEqual(0.5f, output[0], 0.0001f);
        }

        // --- Span オーバーロード（Blend） ---

        [Test]
        public void ResolveBlend_Span_Accumulation_AddsToExistingValues()
        {
            var values = new float[] { 0.4f, 0.3f };
            var output = new float[] { 0.3f, 0.5f };

            ExclusionResolver.ResolveBlend(
                new ReadOnlySpan<float>(values),
                1.0f,
                new Span<float>(output));

            Assert.AreEqual(0.7f, output[0], 0.0001f);
            Assert.AreEqual(0.8f, output[1], 0.0001f);
        }

        // --- 遷移割込（スナップショット） ---

        [Test]
        public void TakeSnapshot_CopiesCurrentValues()
        {
            // 現在の値をスナップショットバッファにコピー
            var currentValues = new float[] { 0.3f, 0.7f, 0.5f };
            var snapshot = new float[3];

            ExclusionResolver.TakeSnapshot(currentValues, snapshot);

            Assert.AreEqual(0.3f, snapshot[0], 0.0001f);
            Assert.AreEqual(0.7f, snapshot[1], 0.0001f);
            Assert.AreEqual(0.5f, snapshot[2], 0.0001f);
        }

        [Test]
        public void TakeSnapshot_OriginalModification_DoesNotAffectSnapshot()
        {
            // スナップショット後に元の値を変更しても影響なし
            var currentValues = new float[] { 0.3f, 0.7f };
            var snapshot = new float[2];

            ExclusionResolver.TakeSnapshot(currentValues, snapshot);

            currentValues[0] = 0.9f;
            currentValues[1] = 0.1f;

            Assert.AreEqual(0.3f, snapshot[0], 0.0001f);
            Assert.AreEqual(0.7f, snapshot[1], 0.0001f);
        }

        [Test]
        public void TakeSnapshot_EmptyArray_NoException()
        {
            var currentValues = Array.Empty<float>();
            var snapshot = Array.Empty<float>();

            Assert.DoesNotThrow(() => ExclusionResolver.TakeSnapshot(currentValues, snapshot));
        }

        [Test]
        public void TransitionInterrupt_SnapshotToNewTarget_CorrectInterpolation()
        {
            // 遷移割込: スナップショットから新ターゲットへの補間
            // 1. 最初の遷移中（A→B、weight=0.5）の現在値をスナップショット
            var fromA = new float[] { 0.0f, 1.0f };
            var toB = new float[] { 1.0f, 0.0f };
            var currentValues = new float[2];

            ExclusionResolver.ResolveLastWins(fromA, toB, 0.5f, currentValues);
            // currentValues = { 0.5, 0.5 }

            // 2. スナップショットを取得
            var snapshot = new float[2];
            ExclusionResolver.TakeSnapshot(currentValues, snapshot);

            // 3. 新しい遷移（スナップショット → C）を開始
            var toC = new float[] { 0.0f, 0.0f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(snapshot, toC, 0.5f, output);

            // lerp(0.5, 0.0, 0.5) = 0.25
            Assert.AreEqual(0.25f, output[0], 0.0001f);
            Assert.AreEqual(0.25f, output[1], 0.0001f);
        }

        [Test]
        public void TransitionInterrupt_SnapshotToNewTarget_WeightZero_ReturnsSnapshot()
        {
            // 遷移割込直後（weight=0）はスナップショットの値
            var snapshot = new float[] { 0.4f, 0.6f };
            var newTarget = new float[] { 0.8f, 0.2f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(snapshot, newTarget, 0f, output);

            Assert.AreEqual(0.4f, output[0], 0.0001f);
            Assert.AreEqual(0.6f, output[1], 0.0001f);
        }

        [Test]
        public void TransitionInterrupt_SnapshotToNewTarget_WeightOne_ReturnsNewTarget()
        {
            // 遷移完了（weight=1）は新ターゲットの値
            var snapshot = new float[] { 0.4f, 0.6f };
            var newTarget = new float[] { 0.8f, 0.2f };
            var output = new float[2];

            ExclusionResolver.ResolveLastWins(snapshot, newTarget, 1f, output);

            Assert.AreEqual(0.8f, output[0], 0.0001f);
            Assert.AreEqual(0.2f, output[1], 0.0001f);
        }

        [Test]
        public void TransitionInterrupt_MultipleInterrupts_CorrectChaining()
        {
            // 複数回の遷移割込が正しくチェーンされる
            // A→B 遷移中の割込 → B→C 遷移中の割込
            var fromA = new float[] { 0.0f };
            var toB = new float[] { 1.0f };
            var current = new float[1];

            // A→B: weight=0.3 → current=0.3
            ExclusionResolver.ResolveLastWins(fromA, toB, 0.3f, current);
            Assert.AreEqual(0.3f, current[0], 0.0001f);

            // 割込1: snapshot=0.3
            var snapshot1 = new float[1];
            ExclusionResolver.TakeSnapshot(current, snapshot1);

            // snapshot→C: weight=0.5, C=0.8 → lerp(0.3, 0.8, 0.5) = 0.55
            var toC = new float[] { 0.8f };
            ExclusionResolver.ResolveLastWins(snapshot1, toC, 0.5f, current);
            Assert.AreEqual(0.55f, current[0], 0.0001f);

            // 割込2: snapshot=0.55
            var snapshot2 = new float[1];
            ExclusionResolver.TakeSnapshot(current, snapshot2);

            // snapshot→D: weight=1.0, D=0.0 → lerp(0.55, 0.0, 1.0) = 0.0
            var toD = new float[] { 0.0f };
            ExclusionResolver.ResolveLastWins(snapshot2, toD, 1.0f, current);
            Assert.AreEqual(0.0f, current[0], 0.0001f);
        }

        // --- Span オーバーロード（TakeSnapshot） ---

        [Test]
        public void TakeSnapshot_Span_CopiesCurrentValues()
        {
            var currentValues = new float[] { 0.3f, 0.7f, 0.5f };
            var snapshot = new float[3];

            ExclusionResolver.TakeSnapshot(
                new ReadOnlySpan<float>(currentValues),
                new Span<float>(snapshot));

            Assert.AreEqual(0.3f, snapshot[0], 0.0001f);
            Assert.AreEqual(0.7f, snapshot[1], 0.0001f);
            Assert.AreEqual(0.5f, snapshot[2], 0.0001f);
        }

        // --- Blend + LastWins 混合シナリオ ---

        [Test]
        public void BlendThenLastWins_IndependentResults()
        {
            // Blend と LastWins は独立して動作（異なるレイヤーを想定）
            var blendOutput = new float[2];
            var lastWinsOutput = new float[2];

            // Blend レイヤー: 2つの Expression を加算
            ExclusionResolver.ResolveBlend(new float[] { 0.3f, 0.4f }, 1.0f, blendOutput);
            ExclusionResolver.ResolveBlend(new float[] { 0.2f, 0.3f }, 1.0f, blendOutput);

            // LastWins レイヤー: クロスフェード
            ExclusionResolver.ResolveLastWins(
                new float[] { 1.0f, 0.0f },
                new float[] { 0.0f, 1.0f },
                0.5f,
                lastWinsOutput);

            // 結果は独立
            Assert.AreEqual(0.5f, blendOutput[0], 0.0001f);
            Assert.AreEqual(0.7f, blendOutput[1], 0.0001f);
            Assert.AreEqual(0.5f, lastWinsOutput[0], 0.0001f);
            Assert.AreEqual(0.5f, lastWinsOutput[1], 0.0001f);
        }

        // --- ClearOutput ユーティリティ ---

        [Test]
        public void ClearOutput_ZerosAllValues()
        {
            var output = new float[] { 0.5f, 0.8f, 0.3f };

            ExclusionResolver.ClearOutput(output);

            Assert.AreEqual(0f, output[0], 0.0001f);
            Assert.AreEqual(0f, output[1], 0.0001f);
            Assert.AreEqual(0f, output[2], 0.0001f);
        }

        [Test]
        public void ClearOutput_EmptyArray_NoException()
        {
            var output = Array.Empty<float>();

            Assert.DoesNotThrow(() => ExclusionResolver.ClearOutput(output));
        }

        [Test]
        public void ClearOutput_Span_ZerosAllValues()
        {
            var output = new float[] { 0.5f, 0.8f, 0.3f };

            ExclusionResolver.ClearOutput(new Span<float>(output));

            Assert.AreEqual(0f, output[0], 0.0001f);
            Assert.AreEqual(0f, output[1], 0.0001f);
            Assert.AreEqual(0f, output[2], 0.0001f);
        }
    }
}
