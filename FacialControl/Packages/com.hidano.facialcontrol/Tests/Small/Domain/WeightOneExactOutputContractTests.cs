using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    // 契約テスト:
    /// 1 source が weight=1 でその他 source が weight=0 のとき、レイヤー出力が
    /// その source の値と浮動小数点誤差範囲 (<c>Mathf.Approximately</c> 相当) で一致する。
    /// </summary>
    /// <remarks>
    //: "When a layer has a mixture of input sources with one source at weight 1
    /// and all others at weight 0, the Layer Input Source Blending Service shall output
    /// exactly that source's values (within floating-point tolerance) for that layer."
    /// </remarks>
    [TestFixture]
    public class WeightOneExactOutputContractTests
    {
        /// <summary>
        /// 浮動小数点比較の許容誤差。<c>Mathf.Approximately</c> は <c>max(1e-6, 1e-6 * max(|a|,|b|)) * 8</c>
        /// 相当の比較を行うため、確実に包含するよう余裕を持って <c>1e-6f</c> を基準値とする。
        /// </summary>
        private const float Tolerance = 1e-6f;

        /// <summary>
        /// BlendShape ごとに異なる値を書込む最小 <see cref="IInputSource"/> フェイク。
        /// index ごとの値一致を検証するため、配列ベースで任意パターンを注入できるようにする。
        /// </summary>
        private sealed class PatternValueSource : IInputSource
        {
            private readonly float[] _values;

            public PatternValueSource(string id, float[] values)
            {
                Id = id;
                BlendShapeCount = values.Length;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(values.Length);
                _values = values;
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                int len = Math.Min(output.Length, _values.Length);
                for (int i = 0; i < len; i++)
                {
                    output[i] = _values[i];
                }
                return true;
            }
        }

        private static FacialProfile BuildSingleLayerProfile()
        {
            var layers = new[]
            {
                new LayerDefinition("layer0", priority: 0, ExclusionMode.LastWins),
            };
            return new FacialProfile("1.0", layers: layers);
        }

        [Test]
        public void Aggregate_SingleSourceWeightOne_OutputMatchesSourceValuesExactly(
            [Values(0f, 0.1f, 0.25f, 0.5f, 0.75f, 0.9f, 1.0f)] float representativeValue)
        {
            // 観測完了条件: 代表値について単一 source weight=1 時に出力が source 値と一致する。
            const int blendShapeCount = 4;
            var profile = BuildSingleLayerProfile();

            var pattern = new float[blendShapeCount];
            for (int i = 0; i < blendShapeCount; i++)
            {
                pattern[i] = representativeValue;
            }
            var src = new PatternValueSource("osc", pattern);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            Assert.AreEqual(blendShapeCount, values.Length);
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(representativeValue, values[k], Tolerance,
                    $"weight=1 の単独 source の値がそのまま出力されること " +
                    $"(value={representativeValue}, k={k})");
            }
        }

        [Test]
        public void Aggregate_SingleSourceWeightOne_NonUniformValues_MatchPerIndex()
        {
            // BlendShape ごとに異なる値を持つ source (index パターン) → 出力は index ごとに完全一致。
            // per-index の順序や off-by-one を含む誤りを検出するための代表パターン。
            float[] pattern = { 0.0f, 0.33333334f, 0.66666667f, 1.0f, 0.1234567f, 0.9876543f };
            var profile = BuildSingleLayerProfile();

            var src = new PatternValueSource("osc", pattern);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, pattern.Length, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, pattern.Length);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < pattern.Length; k++)
            {
                Assert.AreEqual(pattern[k], values[k], Tolerance,
                    $"index ごとに異なる source 値も忠実に反映されること (k={k}, expected={pattern[k]})");
            }
        }

        [Test]
        public void Aggregate_MultipleSourcesOnlyFirstHasWeightOne_OutputMatchesFirstSource()
        {
            // 3 source 構成: source 0 weight=1, 他は weight=0 → 出力は source 0 の値と完全一致。
            // 寄与ゼロである source 1 / source 2 の値が混入しないことを検証する。
            float[] selectedPattern = { 0.2f, 0.5f, 0.8f };
            const int blendShapeCount = 3;

            var profile = BuildSingleLayerProfile();

            var selected = new PatternValueSource("osc", selectedPattern);
            var other1 = new PatternValueSource("lipsync",
                new[] { 1f, 1f, 1f });                      // 混入すれば 1.0 になってしまう
            var other2 = new PatternValueSource("analog-blendshape",
                new[] { 0.9f, 0.9f, 0.9f });                // 混入すれば 0.9 程度に寄与する

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, selected),
                (0, 1, other1),
                (0, 2, other2),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(0, 1, 0f);
            weightBuffer.SetWeight(0, 2, 0f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < blendShapeCount; k++)
            {
                Assert.AreEqual(selectedPattern[k], values[k], Tolerance,
                    $"weight=1 の source の値のみが出力され、weight=0 の他 source は混入しない (k={k})");
            }
        }

        [Test]
        public void Aggregate_MultipleSourcesOnlyMiddleHasWeightOne_OutputMatchesMiddleSource()
        {
            // 選択される source が中間 (index 1) のケース。先頭バイアスで「たまたま source 0
            // が返っているだけ」という誤実装を弾くため、別 index の source 選択でも同じ契約が
            // 成立することを確認する。
            float[] selectedPattern = { 0.15f, 0.35f };
            const int blendShapeCount = 2;

            var profile = BuildSingleLayerProfile();

            var other1 = new PatternValueSource("osc",
                new[] { 0.77f, 0.77f });
            var selected = new PatternValueSource("lipsync", selectedPattern);
            var other2 = new PatternValueSource("analog-blendshape",
                new[] { 0.88f, 0.88f });

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, other1),
                (0, 1, selected),
                (0, 2, other2),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0f);
            weightBuffer.SetWeight(0, 1, 1f);
            weightBuffer.SetWeight(0, 2, 0f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < blendShapeCount; k++)
            {
                Assert.AreEqual(selectedPattern[k], values[k], Tolerance,
                    $"中間 source (index 1) のみ weight=1 → その値と完全一致 (k={k})");
            }
        }

        [Test]
        public void Aggregate_MultipleSourcesOnlyLastHasWeightOne_OutputMatchesLastSource()
        {
            // 末尾 source が選択されるケース。ループ終了境界の誤りを検出する。
            float[] selectedPattern = { 0.123f, 0.456f, 0.789f, 1.0f, 0f };
            const int blendShapeCount = 5;

            var profile = BuildSingleLayerProfile();

            var other1 = new PatternValueSource("osc",
                new[] { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f });
            var other2 = new PatternValueSource("lipsync",
                new[] { 0.9f, 0.9f, 0.9f, 0.9f, 0.9f });
            var selected = new PatternValueSource("analog-blendshape", selectedPattern);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, other1),
                (0, 1, other2),
                (0, 2, selected),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0f);
            weightBuffer.SetWeight(0, 1, 0f);
            weightBuffer.SetWeight(0, 2, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < blendShapeCount; k++)
            {
                Assert.AreEqual(selectedPattern[k], values[k], Tolerance,
                    $"末尾 source (index 2) のみ weight=1 → その値と完全一致 (k={k})");
            }
        }

        [Test]
        public void Aggregate_SingleSourceWeightOne_EdgeBoundaryValues_MatchExactly()
        {
            // 境界値 0.0 と 1.0 を含む代表値セット。加重和 = 1.0 * v = v のあと
            // clamp01 が適用されるが、v は [0, 1] に収まっているため clamp で変化してはならない。
            float[] pattern = { 0.0f, 1.0f, 0.0f, 1.0f };
            var profile = BuildSingleLayerProfile();

            var src = new PatternValueSource("osc", pattern);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, pattern.Length, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, pattern.Length);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < pattern.Length; k++)
            {
                Assert.AreEqual(pattern[k], values[k], Tolerance,
                    $"境界値 (0 または 1) も clamp で変化せず、そのまま出力されること (k={k})");
            }
        }
    }
}
