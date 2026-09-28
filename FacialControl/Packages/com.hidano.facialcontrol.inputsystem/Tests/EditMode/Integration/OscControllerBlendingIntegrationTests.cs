using System;
using System.Collections.Generic;
using NUnit.Framework;
using Unity.Profiling;
using Hidano.FacialControl.Adapters.InputSources;
using Hidano.FacialControl.Adapters.OSC;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.EditMode.Integration
{
    /// <summary>
    // EditMode 統合テスト: Fake OSC + Fake Controller の 50/50 合成を
    /// <see cref="LayerInputSourceAggregator"/> → <see cref="LayerBlender"/> パイプラインで
    /// end-to-end に検証する 。
    /// </summary>
    /// <remarks>
    /// <para>
    /// 本テストは要件 Boundary Context のユースケース 1〜3 をパラメトリックに検証する。
    /// <list type="number">
    ///   <item><b>UC1 状況切替</b>:
    ///     ランタイム weight 変更でコントローラ駆動から OSC キャプチャ駆動へ切替えると、
    ///     最終 BlendShape 出力が次フレームで切替わる 。</item>
    ///   <item><b>UC2 重み付きブレンド</b>:
    ///     同レイヤーにコントローラ (Expression トリガー型) と OSC (値提供型) を
    ///     0.5 / 0.5 で配置した時、両者の BlendShape 値が値レベルで加重和 + 最終クランプ
    ///     される 。</item>
    ///   <item><b>UC3 特定入力源固定</b>:
    ///     あるレイヤーで片側ソースの weight=0 を設定した場合、もう片側ソースの値が
    ///     そのままレイヤー出力になる (単独ソース exact 出力契約と一貫)。</item>
    /// </list>
    /// </para>
    /// <para>
    /// 本テストは <see cref="ExpressionTriggerInputSource"/> と
    /// <see cref="OscInputSource"/> の実装を直接使い、モックは <see cref="OscDoubleBuffer"/>
    /// の書込側と <see cref="ManualTimeProvider"/> (時刻決定論化) のみを用いる。
    /// 集約経路は <see cref="LayerInputSourceAggregator.AggregateAndBlend"/> を通すことで
    /// 既存 <see cref="LayerBlender"/> のシグネチャを一切変更せず  最終 BlendShape
    /// 配列までの全パイプラインを検証する。
    /// </para>
    /// <para>
    /// GC ゼロ契約: パイプライン全体を繰り返しても
    /// <see cref="ProfilerRecorder"/> の <c>GC.Alloc</c> が 0 バイトであることを検証する。
    /// weight 変更や Trigger を含まない「集約のみ」のループを対象とする。
    /// </para>
    /// </remarks>
    [TestFixture]
    [SmallTest]
    public class OscControllerBlendingIntegrationTests : SizedTestFixture
    {
        // BlendShape 配置:
        //   index 0: smile       (controller が Expression 経由で駆動)
        //   index 1: mouth_open  (OSC が値提供型として駆動)
        //   index 2: brow        (未使用、ゼロのままであることを確認)
        private static readonly string[] BlendShapeNames = { "smile", "mouth_open", "brow" };
        private const int BlendShapeCount = 3;
        private const int OscMouthIndex = 1;

        private static FacialProfile BuildSingleLayerProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("emotion", priority: 0, ExclusionMode.LastWins),
            };
            var expressions = new[]
            {
                new Expression(
                    id: "smile",
                    name: "smile",
                    layer: "emotion",
                    transitionDuration: 0.2f,
                    transitionCurve: TransitionCurve.Linear,
                    blendShapeValues: new[]
                    {
                        new BlendShapeMapping("smile", 1.0f),
                    }),
            };
            return new FacialProfile("1.0", layers: layers, expressions: expressions);
        }

        /// <summary>
        /// 本テストフィクスチャ共通の構成: 1 レイヤーに input (sourceIdx 0) と
        /// osc (sourceIdx 1) を登録し、<see cref="ExpressionTriggerInputSource"/> で
        /// smile をトリガー + <see cref="OscDoubleBuffer"/> に mouth_open=1.0 を書込む。
        /// </summary>
        private sealed class Harness : IDisposable
        {
            public readonly FacialProfile Profile;
            public readonly ExpressionTriggerInputSource Controller;
            public readonly OscDoubleBuffer OscBuffer;
            public readonly OscInputSource Osc;
            public readonly ManualTimeProvider Time;
            public readonly LayerInputSourceRegistry Registry;
            public readonly LayerInputSourceWeightBuffer WeightBuffer;
            public readonly LayerInputSourceAggregator Aggregator;

            public Harness()
            {
                Profile = BuildSingleLayerProfile();
                Controller = new ExpressionTriggerInputSource(
                    id: InputSourceId.Parse(ExpressionTriggerInputSource.InputReservedId),
                    blendShapeCount: BlendShapeCount,
                    maxStackDepth: 4,
                    exclusionMode: ExclusionMode.LastWins,
                    blendShapeNames: BlendShapeNames,
                    profile: Profile);

                OscBuffer = new OscDoubleBuffer(BlendShapeCount);
                Time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
                Osc = new OscInputSource(OscBuffer, stalenessSeconds: 0f, timeProvider: Time);

                var bindings = new List<(int, int, IInputSource)>
                {
                    (0, 0, Controller),
                    (0, 1, Osc),
                };
                Registry = new LayerInputSourceRegistry(Profile, BlendShapeCount, bindings);
                WeightBuffer = new LayerInputSourceWeightBuffer(
                    Registry.LayerCount, Registry.MaxSourcesPerLayer);
                Aggregator = new LayerInputSourceAggregator(Registry, WeightBuffer, BlendShapeCount);
            }

            /// <summary>OSC 側に mouth_open を指定値で書込み Swap する。</summary>
            public void PushOscMouthOpen(float value)
            {
                OscBuffer.Write(OscMouthIndex, value);
                OscBuffer.Swap();
            }

            public void Dispose()
            {
                Aggregator?.SetVerboseLogging(false);
                WeightBuffer?.Dispose();
                Registry?.Dispose();
                OscBuffer?.Dispose();
            }
        }

        // ------------------------------------------------------------------
        // UC2 重み付きブレンド (50/50) — fixture の主シナリオ
        // ------------------------------------------------------------------

        [Test]
        public void UC2_WeightedBlend_ControllerAndOsc_FiftyFifty_BlendsAtValueLevel()
        {
            using var h = new Harness();

            h.Controller.TriggerOn("smile");
            h.PushOscMouthOpen(1.0f);

            h.WeightBuffer.SetWeight(0, 0, 0.5f);
            h.WeightBuffer.SetWeight(0, 1, 0.5f);

            Span<int> priorities = stackalloc int[1] { 0 };
            Span<float> layerWeights = stackalloc float[1] { 1.0f };
            Span<float> finalOutput = stackalloc float[BlendShapeCount];

            // Tick を 1.0s 進めて Controller 側 transition (0.2s) を完走させる。
            h.Aggregator.AggregateAndBlend(
                deltaTime: 1.0f,
                priorities: priorities,
                layerWeights: layerWeights,
                finalOutput: finalOutput);

            // 手計算:
            //   intra-layer: clamp01(0.5*[1,0,0] + 0.5*[0,1,0]) = [0.5, 0.5, 0]
            //   inter-layer: 単一レイヤー × layerWeight=1.0 → そのまま最終出力
            Assert.AreEqual(0.5f, finalOutput[0], 1e-5f, "smile: 0.5 * controller + 0.5 * 0");
            Assert.AreEqual(0.5f, finalOutput[1], 1e-5f, "mouth_open: 0.5 * 0 + 0.5 * osc");
            Assert.AreEqual(0.0f, finalOutput[2], 1e-5f, "brow: どちらも書かないのでゼロ");
        }

        [Test]
        public void UC2_WeightedBlend_SumExceedsOne_IsClampedToOne()
        {
            // 両 source が同じ BlendShape に値を提供し、weight 合計も 1 を超える場合、
            // 加重和 > 1 でも最終クランプで 1.0 に収まる 。
            using var h = new Harness();

            // 両者が mouth_open に 1.0 を提供する状況を作る (OSC はそのまま、
            // Controller は smile の代わりに mouth_open BlendShape を持つ Expression を
            // 使いたいが fixture 固定なので、ここでは OSC 側に smile を書込み両 source を
            // 同一 BlendShape で競合させる)。
            h.OscBuffer.Write(0, 1.0f);            // OSC が smile=1.0
            h.OscBuffer.Write(OscMouthIndex, 1.0f);
            h.OscBuffer.Swap();

            h.Controller.TriggerOn("smile");       // Controller が smile=1.0

            h.WeightBuffer.SetWeight(0, 0, 0.8f);  // controller w=0.8
            h.WeightBuffer.SetWeight(0, 1, 0.8f);  // osc w=0.8, sum=1.6>1

            Span<int> priorities = stackalloc int[1] { 0 };
            Span<float> layerWeights = stackalloc float[1] { 1.0f };
            Span<float> finalOutput = stackalloc float[BlendShapeCount];

            h.Aggregator.AggregateAndBlend(
                deltaTime: 1.0f,
                priorities: priorities,
                layerWeights: layerWeights,
                finalOutput: finalOutput);

            // smile: Σ w*v = 0.8*1 + 0.8*1 = 1.6 → clamp to 1.0
            // mouth_open: 0.8*0 + 0.8*1 = 0.8
            Assert.AreEqual(1.0f, finalOutput[0], 1e-5f, "smile: Σw·v=1.6 が 1.0 にクランプ");
            Assert.AreEqual(0.8f, finalOutput[1], 1e-5f, "mouth_open: 0.8 * osc のみ");
        }

        // ------------------------------------------------------------------
        // UC1 状況切替 — ランタイム weight 変更でソースを切替える
        // ------------------------------------------------------------------

        [Test]
        public void UC1_SituationSwitch_ControllerToOsc_AtRuntime_ChangesBlendedOutput()
        {
            using var h = new Harness();

            h.Controller.TriggerOn("smile");
            h.PushOscMouthOpen(1.0f);

            // フェーズ A: controller のみ有効 (weight 1.0 / 0.0)。
            h.WeightBuffer.SetWeight(0, 0, 1.0f);
            h.WeightBuffer.SetWeight(0, 1, 0.0f);

            Span<int> priorities = stackalloc int[1] { 0 };
            Span<float> layerWeights = stackalloc float[1] { 1.0f };
            Span<float> finalOutput = stackalloc float[BlendShapeCount];

            h.Aggregator.AggregateAndBlend(
                deltaTime: 1.0f,
                priorities: priorities,
                layerWeights: layerWeights,
                finalOutput: finalOutput);

            Assert.AreEqual(1.0f, finalOutput[0], 1e-5f,
                "フェーズ A: controller が smile=1.0 を完全駆動");
            Assert.AreEqual(0.0f, finalOutput[1], 1e-5f,
                "フェーズ A: OSC の寄与はゼロ (weight=0)");

            // フェーズ B: OSC のみ有効 (weight 0.0 / 1.0)。同 fixture のまま runtime で切替。
            h.WeightBuffer.SetWeight(0, 0, 0.0f);
            h.WeightBuffer.SetWeight(0, 1, 1.0f);

            h.Aggregator.AggregateAndBlend(
                deltaTime: 0.016f,
                priorities: priorities,
                layerWeights: layerWeights,
                finalOutput: finalOutput);

            Assert.AreEqual(0.0f, finalOutput[0], 1e-5f,
                "フェーズ B: controller の寄与はゼロ (weight=0)");
            Assert.AreEqual(1.0f, finalOutput[1], 1e-5f,
                "フェーズ B: OSC が mouth_open=1.0 を完全駆動");
        }

        // ------------------------------------------------------------------
        // UC3 特定入力源固定 — 片側 weight=0 の固定シナリオ
        // ------------------------------------------------------------------

        [Test]
        public void UC3_FixedSingleSource_OscOnly_ControllerWeightZero_OscValueIsExactOutput()
        {
            // 「lipsync レイヤーは常に uLipSync のみ有効、OSC の口パラメータは無視」を抽象化:
            // レイヤーにコントローラと OSC を両方登録するが controller weight=0 に固定する。
            // OSC の値だけが最終出力に現れる。
            using var h = new Harness();

            h.Controller.TriggerOn("smile"); // 無視されるべき駆動
            h.PushOscMouthOpen(0.7f);

            h.WeightBuffer.SetWeight(0, 0, 0.0f);
            h.WeightBuffer.SetWeight(0, 1, 1.0f);

            Span<int> priorities = stackalloc int[1] { 0 };
            Span<float> layerWeights = stackalloc float[1] { 1.0f };
            Span<float> finalOutput = stackalloc float[BlendShapeCount];

            h.Aggregator.AggregateAndBlend(
                deltaTime: 1.0f,
                priorities: priorities,
                layerWeights: layerWeights,
                finalOutput: finalOutput);

            Assert.AreEqual(0.0f, finalOutput[0], 1e-5f,
                "固定: controller は weight=0 で完全に無視される");
            Assert.AreEqual(0.7f, finalOutput[1], 1e-5f,
                "固定: OSC 値がそのまま最終出力に一致する (floating-point tolerance)");
            Assert.AreEqual(0.0f, finalOutput[2], 1e-5f);
        }

        [Test]
        public void UC3_FixedSingleSource_ControllerOnly_OscWeightZero_ControllerValueIsExactOutput()
        {
            // 対称ケース: OSC weight=0 / controller weight=1 で controller 単独駆動。
            using var h = new Harness();

            h.Controller.TriggerOn("smile");
            h.PushOscMouthOpen(1.0f); // 無視されるべき駆動

            h.WeightBuffer.SetWeight(0, 0, 1.0f);
            h.WeightBuffer.SetWeight(0, 1, 0.0f);

            Span<int> priorities = stackalloc int[1] { 0 };
            Span<float> layerWeights = stackalloc float[1] { 1.0f };
            Span<float> finalOutput = stackalloc float[BlendShapeCount];

            h.Aggregator.AggregateAndBlend(
                deltaTime: 1.0f,
                priorities: priorities,
                layerWeights: layerWeights,
                finalOutput: finalOutput);

            Assert.AreEqual(1.0f, finalOutput[0], 1e-5f,
                "固定: controller smile 値のみが最終出力に反映");
            Assert.AreEqual(0.0f, finalOutput[1], 1e-5f,
                "固定: OSC weight=0 で寄与ゼロ");
        }

        // ------------------------------------------------------------------
        // GC ゼロ契約: パイプライン全体が per-frame 0-alloc であること
        // ------------------------------------------------------------------

        // ProfilerRecorder("GC.Alloc") を現在スレッドのみで記録し、Editor / NUnit の
        // 別スレッド由来ノイズを拾わずに測定対象ループの managed allocation を検出する。
        // 配列は測定開始前に確保し、ループ内では AggregateAndBlend の hot path のみを実行する。
        private const int ZeroAllocMeasureIterations = 50_000;

        [Test]
        public void Pipeline_AggregateAndBlendLoop_AllocatesZeroManagedMemory()
        {
            using var h = new Harness();

            h.Controller.TriggerOn("smile");
            h.PushOscMouthOpen(1.0f);
            h.WeightBuffer.SetWeight(0, 0, 0.5f);
            h.WeightBuffer.SetWeight(0, 1, 0.5f);

            // ウォームアップ: JIT / Id キャッシュ / StringBuilder などの初回確保を排除。
            // ※ stackalloc は ref-like なので測定 API と相性が悪く、
            //    warmup と measure 双方で一旦 heap 配列に載せる。
            int[] prioritiesArr = new[] { 0 };
            float[] layerWeightsArr = new[] { 1.0f };
            float[] finalOutputArr = new float[BlendShapeCount];

            for (int i = 0; i < 10; i++)
            {
                h.Aggregator.AggregateAndBlend(
                    deltaTime: 1.0f,
                    priorities: prioritiesArr,
                    layerWeights: layerWeightsArr,
                    finalOutput: finalOutputArr);
            }

            using var recorder = ProfilerRecorder.StartNew(
                ProfilerCategory.Memory,
                "GC.Alloc",
                1,
                ProfilerRecorderOptions.SumAllSamplesInFrame
                    | ProfilerRecorderOptions.CollectOnlyOnCurrentThread);

            for (int i = 0; i < ZeroAllocMeasureIterations; i++)
            {
                h.Aggregator.AggregateAndBlend(
                    deltaTime: 0.016f,
                    priorities: prioritiesArr,
                    layerWeights: layerWeightsArr,
                    finalOutput: finalOutputArr);
            }

            long gcAllocBytes = recorder.LastValue;

            Assert.That(
                gcAllocBytes,
                Is.LessThanOrEqualTo(0L),
                $"AggregateAndBlend {ZeroAllocMeasureIterations} 回ループで managed alloc 検出: {gcAllocBytes} bytes");
        }
    }
}
