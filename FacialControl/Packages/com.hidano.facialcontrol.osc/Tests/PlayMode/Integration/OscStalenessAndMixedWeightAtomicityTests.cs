using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Application.UseCases;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Integration
{
    /// <summary>
    // PlayMode 統合テスト: 実 <see cref="UnityTimeProvider"/> 下での OSC staleness
    /// タイムアウト動作と、同フレーム内における Bulk / Single Set 混在の atomic 観測を検証する。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 観測完了条件:
    /// </para>
    /// <list type="bullet">
    ///   <item>実 <see cref="UnityTimeProvider"/> 下で <c>stalenessSeconds = 1.0</c> の OSC 入力が
    ///   1 秒超過した時点で <see cref="OscInputSource.TryWriteValues"/> が false を返すこと 。</item>
    ///   <item>同フレーム内で <see cref="LayerUseCase.BeginInputSourceWeightBatch"/> 経由の Bulk 書込と
    ///   <see cref="LayerUseCase.SetInputSourceWeight"/> 経由の Single 書込を混在させても、
    ///   次フレームの <see cref="LayerUseCase.UpdateWeights"/> 実行後に全書込が期待どおりに
    ///   BlendShape 出力へ反映されること 。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class OscStalenessAndMixedWeightAtomicityTests : SizedTestFixture
    {
        // ================================================================
        // 実 UnityTimeProvider 下で staleness 超過 → IsValid=false
        // ================================================================

        [UnityTest]
        public IEnumerator OscInputSource_WithUnityTimeProvider_ExceedsOneSecond_ReturnsFalse()
        {
            using var buffer = new OscDoubleBuffer(2);
            ITimeProvider timeProvider = new UnityTimeProvider();
            var source = new OscInputSource(buffer, stalenessSeconds: 1.0f, timeProvider: timeProvider);

            buffer.Write(0, 0.5f);
            buffer.Write(1, 0.25f);
            buffer.Swap();

            var output = new float[2];
            double observedAt = timeProvider.UnscaledTimeSeconds;

            // 受信直後は WriteTick の差分で _lastDataTime が更新され有効。
            Assert.IsTrue(source.TryWriteValues(output),
                "受信直後は stalenessSeconds=1.0 の閾値内で IsValid=true を返すこと。");
            Assert.AreEqual(0.5f, output[0], 1e-5f);
            Assert.AreEqual(0.25f, output[1], 1e-5f);

            // 実時間で 1 秒超 (閾値 + マージン) 待機し、新規 OSC 受信は行わない。
            yield return new WaitForSecondsRealtime(1.2f);

            double elapsed = timeProvider.UnscaledTimeSeconds - observedAt;
            Assert.Greater(elapsed, 1.0,
                $"前提: 実時間で 1 秒超 (観測値 {elapsed:F3}s) が経過していること。");

            // output を sentinel で汚しておき、false 時に変更されないことも確認する。
            output[0] = 99f;
            output[1] = 99f;
            bool wrote = source.TryWriteValues(output);

            Assert.IsFalse(wrote,
                "実 UnityTimeProvider 下で staleness 1 秒を超過したら IsValid=false を返すこと 。");
            Assert.AreEqual(99f, output[0], 1e-5f,
                "false を返した場合は output を変更しないこと (IInputSource 契約)。");
            Assert.AreEqual(99f, output[1], 1e-5f);
        }

        // ================================================================
        // 同フレーム内 Bulk + Single 混在の atomic 観測
        // ================================================================

        [UnityTest]
        public IEnumerator MixedBulkAndSingle_DifferentSources_BothReflectedNextFrame()
        {
            string[] blendShapeNames = { "bs_a", "bs_b", "bs_c" };
            var profile = CreateLayerProfileWithExpression("emotion", "expr-1", "bs_a", value: 1.0f);
            var expressionUseCase = new ExpressionUseCase(profile);

            var fakeB = new FakeSelectiveValueSource("osc", blendShapeCount: 3, writeIndex: 1, writeValue: 1.0f);
            var fakeC = new FakeSelectiveValueSource("lipsync", blendShapeCount: 3, writeIndex: 2, writeValue: 1.0f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fakeB, 0.0f),
                (0, fakeC, 0.0f),
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            try
            {
                expressionUseCase.Activate(profile.Expressions.Span[0]);
                layerUseCase.UpdateWeights(0.001f);

                var baseline = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(1.0f, baseline[0], 1e-4f, "前提: source0 (Expression) の bs_a 寄与のみ出ていること。");
                Assert.AreEqual(0.0f, baseline[1], 1e-4f, "前提: FakeB は weight=0 で寄与していないこと。");
                Assert.AreEqual(0.0f, baseline[2], 1e-4f, "前提: FakeC は weight=0 で寄与していないこと。");

                // 同フレーム内で Single と Bulk を混在させる。
                // (a) Single: source1 (FakeB) を 0.6 へ
                layerUseCase.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.6f);
                // (b) Bulk: source2 (FakeC) を 0.3 へ (Dispose で atomic に flush)
                using (var batch = layerUseCase.BeginInputSourceWeightBatch())
                {
                    batch.SetWeight(0, sourceIdx: 2, weight: 0.3f);
                }

                yield return null;

                layerUseCase.UpdateWeights(0.016f);
                var output = layerUseCase.GetBlendedOutput();

                Assert.AreEqual(1.0f, output[0], 1e-4f, "source0 (Expression) の寄与は維持されること。");
                Assert.AreEqual(0.6f, output[1], 1e-4f,
                    "Single SetInputSourceWeight の 0.6 が FakeB 経由で bs_b に反映されること。");
                Assert.AreEqual(0.3f, output[2], 1e-4f,
                    "BulkScope 経由 commit の 0.3 が FakeC 経由で bs_c に反映されること。");
            }
            finally
            {
                layerUseCase.Dispose();
            }
        }

        [UnityTest]
        public IEnumerator MixedBulkAndSingle_SameSource_LastWriterWins()
        {
            string[] blendShapeNames = { "bs_a", "bs_b" };
            var profile = CreateLayerProfileWithExpression("emotion", "expr-1", "bs_a", value: 1.0f);
            var expressionUseCase = new ExpressionUseCase(profile);

            var fakeB = new FakeSelectiveValueSource("osc", blendShapeCount: 2, writeIndex: 1, writeValue: 1.0f);
            var additional = new List<(int layerIdx, IInputSource source, float weight)>
            {
                (0, fakeB, 0.0f),
            };
            var layerUseCase = new LayerUseCase(profile, expressionUseCase, blendShapeNames, additional);

            try
            {
                expressionUseCase.Activate(profile.Expressions.Span[0]);
                layerUseCase.UpdateWeights(0.001f);

                // ケース 1: Single → Bulk の順で同 source を上書き。Bulk commit が writeBuffer を
                //           後から上書きするため last-writer (= Bulk) が勝つ (D-7 仕様)。
                layerUseCase.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.8f);
                using (var batch = layerUseCase.BeginInputSourceWeightBatch())
                {
                    batch.SetWeight(0, sourceIdx: 1, weight: 0.2f);
                }

                yield return null;

                layerUseCase.UpdateWeights(0.016f);
                var output1 = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(0.2f, output1[1], 1e-4f,
                    "Single 先行 + Bulk 後続の場合、Bulk commit が writeBuffer を上書きし last-writer-wins となること 。");

                // ケース 2: Bulk → Single の順で同 source を上書き。Single が直接 writeBuffer に
                //           書込むため last-writer (= Single) が勝つ。
                using (var batch = layerUseCase.BeginInputSourceWeightBatch())
                {
                    batch.SetWeight(0, sourceIdx: 1, weight: 0.4f);
                }
                layerUseCase.SetInputSourceWeight(0, sourceIdx: 1, weight: 0.9f);

                yield return null;

                layerUseCase.UpdateWeights(0.016f);
                var output2 = layerUseCase.GetBlendedOutput();
                Assert.AreEqual(0.9f, output2[1], 1e-4f,
                    "Bulk 先行 + Single 後続の場合、Single の直接書込が勝ち last-writer-wins となること 。");
            }
            finally
            {
                layerUseCase.Dispose();
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
    }
}
