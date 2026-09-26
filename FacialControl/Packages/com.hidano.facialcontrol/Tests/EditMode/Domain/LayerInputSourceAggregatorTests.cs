using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Hidano.FacialControl.Domain.Adapters;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;
using Hidano.FacialControl.Tests.Shared;

namespace Hidano.FacialControl.Tests.EditMode.Domain
{
    /// <summary>
    /// <see cref="LayerInputSourceAggregator"/> の per-layer 加重和と最終クランプ、
    /// 空レイヤー警告、2 段パイプライン (AggregateAndBlend)、診断スナップショット、verbose ログのレート制限、
    /// layer ContributeMask の OR 集約、および <see cref="ILayerSourceValueObserver"/> への値通知を検証する。
    /// </summary>
    /// <remarks>
    /// 加重和の観測完了条件:
    /// <list type="bullet">
    ///   <item><c>w1=0.5, w2=0.5, v1[k]=1, v2[k]=1</c> → <c>output[k]=1.0</c>。</item>
    ///   <item>Σw·v > 1 の場合でもクランプのみ 。</item>
    ///   <item>3 source × 2 layer の固定値入力に対し <c>output[k] = clamp01(Σ wᵢ · values_i[k])</c> が手計算と一致する 。</item>
    /// </list>
    /// </remarks>
    [TestFixture]
    public class LayerInputSourceAggregatorTests
    {
        /// <summary>
        /// 固定値を書込む最小 <see cref="IInputSource"/> フェイク。
        /// </summary>
        private sealed class FixedValueSource : IInputSource
        {
            private readonly float _value;
            private readonly bool _isValid;

            public FixedValueSource(string id, int blendShapeCount, float value, bool isValid = true)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(blendShapeCount);
                _value = value;
                _isValid = isValid;
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public int TickCallCount { get; private set; }

            public void Tick(float deltaTime)
            {
                TickCallCount++;
            }

            public bool TryWriteValues(Span<float> output)
            {
                if (!_isValid)
                {
                    return false;
                }

                int len = Math.Min(output.Length, BlendShapeCount);
                for (int i = 0; i < len; i++)
                {
                    output[i] = _value;
                }
                return true;
            }
        }

        private static FacialProfile BuildProfile(int layerCount)
        {
            var layers = new LayerDefinition[layerCount];
            for (int i = 0; i < layerCount; i++)
            {
                layers[i] = new LayerDefinition($"layer{i}", priority: i, ExclusionMode.LastWins);
            }
            return new FacialProfile("1.0", layers: layers);
        }

        [Test]
        public void Aggregate_TwoSourcesHalfWeightEach_SumsToOne()
        {
            // 観測完了条件: w1=0.5, w2=0.5, v1=1, v2=1 → output[k] = 1.0
            const int blendShapeCount = 4;
            var profile = BuildProfile(layerCount: 1);

            var s0 = new FixedValueSource("s0", blendShapeCount, value: 1f);
            var s1 = new FixedValueSource("s1", blendShapeCount, value: 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, s0),
                (0, 1, s1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.5f);
            weightBuffer.SetWeight(0, 1, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            Assert.AreEqual(blendShapeCount, values.Length);
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(1.0f, values[k], 1e-6f,
                    $"w1=0.5*1 + w2=0.5*1 = 1.0 に一致すること (k={k})");
            }
        }

        [Test]
        public void Aggregate_SumExceedsOne_IsClamped()
        {
            // Σw·v > 1 でもクランプのみ 
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 1);

            var s0 = new FixedValueSource("s0", blendShapeCount, value: 1f);
            var s1 = new FixedValueSource("s1", blendShapeCount, value: 1f);
            var s2 = new FixedValueSource("s2", blendShapeCount, value: 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, s0),
                (0, 1, s1),
                (0, 2, s2),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(0, 1, 1f);
            weightBuffer.SetWeight(0, 2, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(1.0f, values[k], 1e-6f,
                    $"Σw·v = 3.0 でもクランプで 1.0 に収まること (k={k})");
            }
        }

        [Test]
        public void Aggregate_MixedWeights_MatchesManualComputation()
        {
            // 3 source × 2 layer、異なる値・異なる weight で手計算と一致すること。
            // layer 0: 3 sources, weights (0.1, 0.3, 0.2), values (0.5, 1.0, 0.25)
            //   → 0.1*0.5 + 0.3*1.0 + 0.2*0.25 = 0.05 + 0.3 + 0.05 = 0.4
            // layer 1: 3 sources, weights (0.4, 0.2, 0.1), values (0.25, 0.5, 1.0)
            //   → 0.4*0.25 + 0.2*0.5 + 0.1*1.0 = 0.1 + 0.1 + 0.1 = 0.3
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 2);

            var l0s0 = new FixedValueSource("l0s0", blendShapeCount, value: 0.5f);
            var l0s1 = new FixedValueSource("l0s1", blendShapeCount, value: 1.0f);
            var l0s2 = new FixedValueSource("l0s2", blendShapeCount, value: 0.25f);
            var l1s0 = new FixedValueSource("l1s0", blendShapeCount, value: 0.25f);
            var l1s1 = new FixedValueSource("l1s1", blendShapeCount, value: 0.5f);
            var l1s2 = new FixedValueSource("l1s2", blendShapeCount, value: 1.0f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0), (0, 1, l0s1), (0, 2, l0s2),
                (1, 0, l1s0), (1, 1, l1s1), (1, 2, l1s2),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.1f);
            weightBuffer.SetWeight(0, 1, 0.3f);
            weightBuffer.SetWeight(0, 2, 0.2f);
            weightBuffer.SetWeight(1, 0, 0.4f);
            weightBuffer.SetWeight(1, 1, 0.2f);
            weightBuffer.SetWeight(1, 2, 0.1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var l0Values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < l0Values.Length; k++)
            {
                Assert.AreEqual(0.4f, l0Values[k], 1e-6f,
                    $"layer0: Σ w·v = 0.4 (k={k})");
            }

            var l1Values = outputPerLayer[1].BlendShapeValues.Span;
            for (int k = 0; k < l1Values.Length; k++)
            {
                Assert.AreEqual(0.3f, l1Values[k], 1e-6f,
                    $"layer1: Σ w·v = 0.3 (k={k})");
            }
        }

        [Test]
        public void Aggregate_ZeroWeights_OutputsZero()
        {
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);

            var s0 = new FixedValueSource("s0", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, s0) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(0f, values[k], 1e-6f,
                    $"全ての weight=0 のときは output=0 (k={k})");
            }
        }

        [Test]
        public void Aggregate_CallsTickOnSources()
        {
            // Aggregate は各 source の Tick を毎フレーム 1 回呼ぶ。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);
            var s0 = new FixedValueSource("s0", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, s0) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            aggregator.Aggregate(deltaTime: 0.016f, outputPerLayer);
            aggregator.Aggregate(deltaTime: 0.016f, outputPerLayer);

            Assert.AreEqual(2, s0.TickCallCount,
                "Aggregate 呼出しごとに Tick が 1 回呼ばれること");
        }

        /// <summary>
        /// 書込長を制限できる <see cref="IInputSource"/> フェイク。
        /// <c>writeLength</c> で指定した先頭 N インデックスにのみ値を書込む。
        /// 残余は <see cref="TryWriteValues"/> 内で一切触らず、Aggregator のゼロクリア契約
        /// (overlap-only) を検証するために使う。
        /// </summary>
        private sealed class PartialWriteSource : IInputSource
        {
            private readonly float _value;
            private readonly int _writeLength;

            public PartialWriteSource(string id, int blendShapeCount, int writeLength, float value)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = ContributeMaskTestHelper.AllSetContributeMask(blendShapeCount);
                _writeLength = writeLength;
                _value = value;
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                int len = Math.Min(_writeLength, output.Length);
                for (int i = 0; i < len; i++)
                {
                    output[i] = _value;
                }
                return true;
            }
        }

        [Test]
        public void Aggregate_SourceWritesShorterSpan_UnwrittenIndicesContributeZero()
        {
            // overlap-only: source が BlendShape 個数未満しか書込まない場合、
            // Aggregator 側で scratch が事前ゼロクリアされるため、未書込インデックスの
            // 寄与は 0 になり、前フレームの値が残らない契約を検証する。
            const int blendShapeCount = 4;
            var profile = BuildProfile(layerCount: 1);

            // 先頭 2 indices のみ 1.0 を書込む。残余 (index 2, 3) は触らない。
            var partial = new PartialWriteSource("partial", blendShapeCount, writeLength: 2, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, partial) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            // 1 フレーム目
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            var values1 = outputPerLayer[0].BlendShapeValues.Span;
            Assert.AreEqual(1f, values1[0], 1e-6f, "overlap 範囲の index 0 は書込値");
            Assert.AreEqual(1f, values1[1], 1e-6f, "overlap 範囲の index 1 は書込値");
            Assert.AreEqual(0f, values1[2], 1e-6f, "未書込 index 2 はゼロ (事前クリア済み)");
            Assert.AreEqual(0f, values1[3], 1e-6f, "未書込 index 3 はゼロ (事前クリア済み)");

            // 2 フレーム目 (前フレーム値の leakage が無いことを確認)
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            var values2 = outputPerLayer[0].BlendShapeValues.Span;
            Assert.AreEqual(0f, values2[2], 1e-6f, "前フレーム値が leakage しないこと");
            Assert.AreEqual(0f, values2[3], 1e-6f, "前フレーム値が leakage しないこと");
        }

        [Test]
        public void Aggregate_InvalidSource_ContributesZeroWithoutException()
        {
            // 観測完了条件:
            // 3 source のうち 1 source だけ IsValid=false のとき、残り 2 source の
            // 加重和のみが出力され、例外が発生しないこと。
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 1);

            var valid0 = new FixedValueSource("valid0", blendShapeCount, value: 0.5f);
            var invalid = new FixedValueSource("invalid", blendShapeCount, value: 1f, isValid: false);
            var valid1 = new FixedValueSource("valid1", blendShapeCount, value: 0.25f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, valid0),
                (0, 1, invalid),
                (0, 2, valid1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.4f);
            weightBuffer.SetWeight(0, 1, 1f);    // 無効ソースに何らかの weight が設定されていても
            weightBuffer.SetWeight(0, 2, 0.2f);  // 寄与ゼロであることを確認する。

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            // 無効ソースが混在していても Aggregate は例外を出してはならない
            // (Span<T> は ref struct で Assert.DoesNotThrow のラムダに捕捉できないため直呼び出しで検証)
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var values = outputPerLayer[0].BlendShapeValues.Span;
            // 期待値: 0.4 * 0.5 + 0 * 1 + 0.2 * 0.25 = 0.2 + 0 + 0.05 = 0.25
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(0.25f, values[k], 1e-6f,
                    $"無効ソース (isValid=false) の寄与は 0、残り 2 source の加重和のみ (k={k})");
            }
        }

        // ----- 空レイヤー（source 未登録 / 全 source 無効）の出力はゼロ -----

        [Test]
        public void Aggregate_NoSourcesRegisteredForLayer_OutputsZero()
        {
            // source 登録ゼロのレイヤーは warning を出し（照合はクラスタグとレイヤー番号のみ）、出力はゼロ。
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>();

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, Math.Max(1, registry.MaxSourcesPerLayer));

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceAggregator.*layer 0"));

            for (int frame = 0; frame < 5; frame++)
            {
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(0f, values[k], 1e-6f,
                    $"空レイヤーの出力はゼロであること (k={k})");
            }
        }

        [Test]
        public void Aggregate_AllSourcesInvalid_OutputsZero()
        {
            // 全 source が IsValid=false のレイヤーも空レイヤー扱いで出力はゼロ。
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 1);

            var invalid0 = new FixedValueSource("invalid0", blendShapeCount, value: 1f, isValid: false);
            var invalid1 = new FixedValueSource("invalid1", blendShapeCount, value: 0.5f, isValid: false);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, invalid0),
                (0, 1, invalid1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(0, 1, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceAggregator.*layer 0"));

            for (int frame = 0; frame < 10; frame++)
            {
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(0f, values[k], 1e-6f,
                    $"全 source が IsValid=false のレイヤー出力はゼロであること (k={k})");
            }
        }

        [Test]
        public void Aggregate_EmptyLayerCoexistsWithValidLayer_OnlyValidLayerContributes()
        {
            // 2 レイヤー構成: layer 0 は valid、layer 1 は全 source 無効。
            // layer 0 は通常どおり出力され、layer 1 はゼロ出力になる。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 2);

            var valid = new FixedValueSource("valid", blendShapeCount, value: 0.5f);
            var invalid = new FixedValueSource("invalid", blendShapeCount, value: 1f, isValid: false);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, valid),
                (1, 0, invalid),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(1, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceAggregator.*layer 1"));

            for (int frame = 0; frame < 3; frame++)
            {
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }

            var layer0Values = outputPerLayer[0].BlendShapeValues.Span;
            var layer1Values = outputPerLayer[1].BlendShapeValues.Span;
            for (int k = 0; k < layer0Values.Length; k++)
            {
                Assert.AreEqual(0.5f, layer0Values[k], 1e-6f,
                    $"valid レイヤーは通常通り出力 (k={k})");
                Assert.AreEqual(0f, layer1Values[k], 1e-6f,
                    $"空レイヤー出力はゼロ (k={k})");
            }
        }

        [Test]
        public void Aggregate_ValidSourceWithZeroWeight_OutputsZero()
        {
            // IsValid=true だが weight=0 のソースは「空レイヤー」ではなく、寄与 0 として扱われる。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);

            var valid = new FixedValueSource("valid", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, valid) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            // weight=0 のまま。IsValid=true だが寄与は 0。
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            for (int frame = 0; frame < 5; frame++)
            {
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }

            var values = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < values.Length; k++)
            {
                Assert.AreEqual(0f, values[k], 1e-6f,
                    $"weight=0 なので出力はゼロ (k={k})");
            }
        }

        [Test]
        public void Constructor_NullRegistry_Throws()
        {
            using var weightBuffer = new LayerInputSourceWeightBuffer(1, 1);
            Assert.Throws<ArgumentNullException>(() =>
                new LayerInputSourceAggregator(null, weightBuffer, blendShapeCount: 4));
        }

        [Test]
        public void Constructor_NullWeightBuffer_Throws()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>();
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 4, bindings);

            Assert.Throws<ArgumentNullException>(() =>
                new LayerInputSourceAggregator(registry, null, blendShapeCount: 4));
        }

        [Test]
        public void Constructor_NegativeBlendShapeCount_Throws()
        {
            var profile = BuildProfile(layerCount: 1);
            var bindings = new List<(int, int, IInputSource)>();
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount: 0, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(1, 1);

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount: -1));
        }

        // ----- 2 段パイプライン: Aggregator → LayerBlender.Blend 接続 -----

        [Test]
        public void Aggregate_WithPrioritiesAndLayerWeights_AppliesThemToLayerInputs()
        {
            // priorities / layerWeights が LayerInput.Priority / LayerInput.Weight に直接載ること。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 2);

            var l0s0 = new FixedValueSource("l0s0", blendShapeCount, value: 0.5f);
            var l1s0 = new FixedValueSource("l1s0", blendShapeCount, value: 0.25f);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(1, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];

            ReadOnlySpan<int> priorities = new int[] { 42, 7 };
            ReadOnlySpan<float> layerWeights = new float[] { 0.25f, 0.75f };

            aggregator.Aggregate(deltaTime: 0f, priorities, layerWeights, outputPerLayer);

            Assert.AreEqual(42, outputPerLayer[0].Priority, "layer 0 の priority はスパンから転写される");
            Assert.AreEqual(7, outputPerLayer[1].Priority, "layer 1 の priority はスパンから転写される");
            Assert.AreEqual(0.25f, outputPerLayer[0].Weight, 1e-6f, "layer 0 の inter-layer weight");
            Assert.AreEqual(0.75f, outputPerLayer[1].Weight, 1e-6f, "layer 1 の inter-layer weight");
        }

        [Test]
        public void Aggregate_SourceWeightAndLayerWeight_AreAppliedIndependently()
        {
            // source weight は intra-layer の加重和だけに効き、inter-layer weight とは乗算されない。
            // Aggregator の per-layer 出力は source weight だけで決まり、inter-layer weight は
            // LayerInput.Weight にそのまま載るだけであること。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);

            var src = new FixedValueSource("s0", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            // source weight = 0.4。inter-layer weight = 0.5。
            // 独立適用なので per-layer 出力は 0.4、LayerInput.Weight は 0.5 でなければならない。
            weightBuffer.SetWeight(0, 0, 0.4f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            ReadOnlySpan<int> priorities = new int[] { 0 };
            ReadOnlySpan<float> layerWeights = new float[] { 0.5f };

            aggregator.Aggregate(deltaTime: 0f, priorities, layerWeights, outputPerLayer);

            var layerValues = outputPerLayer[0].BlendShapeValues.Span;
            for (int k = 0; k < layerValues.Length; k++)
            {
                // per-layer バッファは source weight のみ適用 (0.4)。inter-layer weight は乗算しない。
                Assert.AreEqual(0.4f, layerValues[k], 1e-6f,
                    $"source weight のみが per-layer 出力に適用されること (k={k})");
            }
            Assert.AreEqual(0.5f, outputPerLayer[0].Weight, 1e-6f,
                "inter-layer weight は LayerInput.Weight に独立に載ること");
        }

        [Test]
        public void AggregateAndBlend_TwoLayersIndependentWeights_MatchesReferenceLayerBlender()
        {
            // 観測完了条件: per-layer 集約値が inter-layer blend を経由して最終出力に届くこと。
            // 既存 LayerBlender の挙動を壊さないこと (参照実装との一致)。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 2);

            // layer 0: source weight 1.0 × value 1.0 = 1.0
            // layer 1: source weight 0.5 × value 1.0 = 0.5
            var l0s0 = new FixedValueSource("l0s0", blendShapeCount, value: 1f);
            var l1s0 = new FixedValueSource("l1s0", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(1, 0, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);

            // inter-layer: layer 0 が低優先 (基底)、layer 1 が高優先 (上書き)。
            // layer1.Weight=0.5 で lerp(1.0, 0.5, 0.5) = 0.75 を期待。
            ReadOnlySpan<int> priorities = new int[] { 0, 1 };
            ReadOnlySpan<float> layerWeights = new float[] { 1f, 0.5f };

            Span<float> finalOutput = stackalloc float[blendShapeCount];
            aggregator.AggregateAndBlend(deltaTime: 0f, priorities, layerWeights, finalOutput);

            // 参照: 同じ per-layer 値と weight で LayerBlender.Blend を直接呼んだ結果。
            var referenceLayers = new LayerBlender.LayerInput[]
            {
                new LayerBlender.LayerInput(
                    priority: 0, weight: 1f, blendShapeValues: new float[] { 1f, 1f }),
                new LayerBlender.LayerInput(
                    priority: 1, weight: 0.5f, blendShapeValues: new float[] { 0.5f, 0.5f }),
            };
            Span<float> referenceOutput = stackalloc float[blendShapeCount];
            LayerBlender.Blend(referenceLayers, referenceOutput);

            for (int k = 0; k < blendShapeCount; k++)
            {
                Assert.AreEqual(0.75f, finalOutput[k], 1e-6f,
                    $"2 段パイプラインの最終出力 (k={k})");
                Assert.AreEqual(referenceOutput[k], finalOutput[k], 1e-6f,
                    $"参照 LayerBlender.Blend と完全一致すること (k={k})");
            }
        }

        [Test]
        public void AggregateAndBlend_SourceWeightDoesNotMultiplyIntoLayerWeight()
        {
            // source weight と LayerInput.Weight が乗算されない独立適用を最終出力レベルで検証。
            // レイヤー 1 本構成なら LayerBlender.Blend は basis として values*weight を clamp01 する
            // (LayerBlender.cs:85 "output[i] = Clamp01(firstValues[i] * firstWeight)")。
            //   final = clamp01(perLayer * layerWeight)
            //         = clamp01((source * sourceWeight) * layerWeight)
            // もし「source weight が layer weight と乗算」されていれば perLayer に layerWeight が
            // 既に入ってしまい、最終出力は source * sourceWeight * layerWeight^2 になって過小になる。
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 1);

            var src = new FixedValueSource("s0", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            const float sourceWeight = 0.4f;
            const float layerWeight = 0.5f;
            weightBuffer.SetWeight(0, 0, sourceWeight);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            ReadOnlySpan<int> priorities = new int[] { 0 };
            ReadOnlySpan<float> layerWeights = new float[] { layerWeight };

            Span<float> finalOutput = stackalloc float[blendShapeCount];
            aggregator.AggregateAndBlend(deltaTime: 0f, priorities, layerWeights, finalOutput);

            // 期待値: clamp01(1.0 * 0.4) * 0.5 = 0.4 * 0.5 = 0.20
            // もし乗算されていれば: clamp01(0.4 * 0.5) * 0.5 = 0.2 * 0.5 = 0.10 → 不一致で失敗
            Assert.AreEqual(0.20f, finalOutput[0], 1e-6f,
                "source weight と layer weight は独立適用される (乗算されない)");
        }

        [Test]
        public void AggregateAndBlend_IsStableAcrossMultipleInvocations()
        {
            // 同一入力で複数回呼出しても安定した結果を返す (内部 LayerInput[] スクラッチが
            // 前フレームの値に影響されないこと、GC-free 再利用の回帰防止)。
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 2);

            var l0 = new FixedValueSource("l0", blendShapeCount, value: 1f);
            var l1 = new FixedValueSource("l1", blendShapeCount, value: 0.5f);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0),
                (1, 0, l1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 1f);
            weightBuffer.SetWeight(1, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            ReadOnlySpan<int> priorities = new int[] { 0, 1 };
            ReadOnlySpan<float> layerWeights = new float[] { 1f, 1f };

            Span<float> out1 = stackalloc float[blendShapeCount];
            Span<float> out2 = stackalloc float[blendShapeCount];
            aggregator.AggregateAndBlend(deltaTime: 0f, priorities, layerWeights, out1);
            aggregator.AggregateAndBlend(deltaTime: 0f, priorities, layerWeights, out2);

            // layer 1 が weight=1 で完全上書き → 0.5
            for (int k = 0; k < blendShapeCount; k++)
            {
                Assert.AreEqual(0.5f, out1[k], 1e-6f, $"1 回目の出力 (k={k})");
                Assert.AreEqual(out1[k], out2[k], 1e-6f, $"繰返し呼出しで安定 (k={k})");
            }
        }

        [Test]
        public void Aggregate_PrioritiesSpanTooShort_Throws()
        {
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 2);
            var bindings = new List<(int, int, IInputSource)>();

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, Math.Max(1, registry.MaxSourcesPerLayer));
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);

            var priorities = new int[] { 0 };           // 長さ 1 < LayerCount=2
            var layerWeights = new float[] { 1f, 1f };
            var output = new LayerBlender.LayerInput[2];
            Assert.Throws<ArgumentException>(() =>
            {
                aggregator.Aggregate(
                    deltaTime: 0f,
                    new ReadOnlySpan<int>(priorities),
                    new ReadOnlySpan<float>(layerWeights),
                    new Span<LayerBlender.LayerInput>(output));
            });
        }

        [Test]
        public void Aggregate_LayerWeightsSpanTooShort_Throws()
        {
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 2);
            var bindings = new List<(int, int, IInputSource)>();

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, Math.Max(1, registry.MaxSourcesPerLayer));
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);

            var priorities = new int[] { 0, 1 };
            var layerWeights = new float[] { 1f };      // 長さ 1 < LayerCount=2
            var output = new LayerBlender.LayerInput[2];
            Assert.Throws<ArgumentException>(() =>
            {
                aggregator.Aggregate(
                    deltaTime: 0f,
                    new ReadOnlySpan<int>(priorities),
                    new ReadOnlySpan<float>(layerWeights),
                    new Span<LayerBlender.LayerInput>(output));
            });
        }

        // ----- 診断スナップショット API (TryWriteSnapshot / GetSnapshot) -----

        [Test]
        public void TryWriteSnapshot_AfterAggregate_ReflectsCurrentWeightsAndValidity()
        {
            // 観測完了条件: 直近 Aggregate で反映された weight / isValid が snapshot に現れる。
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);

            var valid = new FixedValueSource("osc", blendShapeCount, value: 0.5f);
            var invalid = new FixedValueSource("lipsync", blendShapeCount, value: 1f, isValid: false);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, valid),
                (0, 1, invalid),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.4f);
            weightBuffer.SetWeight(0, 1, 0.3f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Span<LayerSourceWeightEntry> snapshot = new LayerSourceWeightEntry[8];
            Assert.IsTrue(
                aggregator.TryWriteSnapshot(snapshot, out int written),
                "十分な buffer 長なら TryWriteSnapshot は true を返す");
            Assert.AreEqual(2, written, "登録されている 2 source 分のエントリが書込まれる");

            Assert.AreEqual(0, snapshot[0].LayerIdx);
            Assert.AreEqual(InputSourceId.Parse("osc"), snapshot[0].SourceId);
            Assert.AreEqual(0.4f, snapshot[0].Weight, 1e-6f);
            Assert.IsTrue(snapshot[0].IsValid, "valid source は IsValid=true を報告する");
            Assert.IsFalse(snapshot[0].Saturated, "Σw=0.4+0.3=0.7 <= 1 なので Saturated=false");

            Assert.AreEqual(0, snapshot[1].LayerIdx);
            Assert.AreEqual(InputSourceId.Parse("lipsync"), snapshot[1].SourceId);
            Assert.AreEqual(0.3f, snapshot[1].Weight, 1e-6f);
            Assert.IsFalse(snapshot[1].IsValid, "invalid source は IsValid=false を報告する");
            Assert.IsFalse(snapshot[1].Saturated, "Σw は invalid source を除外した 0.4 < 1 なので Saturated=false");
        }

        [Test]
        public void TryWriteSnapshot_ReflectsWeightChangesAcrossFrames()
        {
            // 観測完了条件: SetWeight 後の次 Aggregate で snapshot が更新されること。
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 1);

            var src = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, src) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.25f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Span<LayerSourceWeightEntry> snapshot = new LayerSourceWeightEntry[4];
            Assert.IsTrue(aggregator.TryWriteSnapshot(snapshot, out int written1));
            Assert.AreEqual(1, written1);
            Assert.AreEqual(0.25f, snapshot[0].Weight, 1e-6f);

            // 次フレームで weight を変更し、再 Aggregate 後に snapshot が更新されていること。
            weightBuffer.SetWeight(0, 0, 0.75f);
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            Assert.IsTrue(aggregator.TryWriteSnapshot(snapshot, out int written2));
            Assert.AreEqual(1, written2);
            Assert.AreEqual(0.75f, snapshot[0].Weight, 1e-6f,
                "SetWeight → 次 Aggregate の順で snapshot が更新されること");
        }

        [Test]
        public void TryWriteSnapshot_WeightSumExceedsOne_MarksAllEntriesSaturated()
        {
            // 観測完了条件: Σw > 1 のレイヤーでは、当該レイヤーの全エントリの Saturated=true。
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 2);

            var l0s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var l0s1 = new FixedValueSource("lipsync", blendShapeCount, value: 1f);
            var l0s2 = new FixedValueSource("input", blendShapeCount, value: 1f);
            var l1s0 = new FixedValueSource("analog-blendshape", blendShapeCount, value: 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (0, 1, l0s1),
                (0, 2, l0s2),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            // layer 0: Σw = 0.5 + 0.5 + 0.5 = 1.5 → Saturated
            weightBuffer.SetWeight(0, 0, 0.5f);
            weightBuffer.SetWeight(0, 1, 0.5f);
            weightBuffer.SetWeight(0, 2, 0.5f);
            // layer 1: Σw = 0.5 → not saturated
            weightBuffer.SetWeight(1, 0, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Span<LayerSourceWeightEntry> snapshot = new LayerSourceWeightEntry[8];
            Assert.IsTrue(aggregator.TryWriteSnapshot(snapshot, out int written));
            Assert.AreEqual(4, written);

            for (int i = 0; i < written; i++)
            {
                if (snapshot[i].LayerIdx == 0)
                {
                    Assert.IsTrue(snapshot[i].Saturated,
                        $"layer 0 (Σw=1.5) の全エントリは Saturated=true (i={i})");
                }
                else
                {
                    Assert.IsFalse(snapshot[i].Saturated,
                        $"layer 1 (Σw=0.5) のエントリは Saturated=false (i={i})");
                }
            }
        }

        [Test]
        public void TryWriteSnapshot_BufferTooShort_ReturnsFalseAndWrittenZero()
        {
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 1);

            var s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var s1 = new FixedValueSource("lipsync", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, s0),
                (0, 1, s1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.5f);
            weightBuffer.SetWeight(0, 1, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            // buffer 長 1 < 書込予定 2 なので false + written=0。
            Span<LayerSourceWeightEntry> tooShort = new LayerSourceWeightEntry[1];
            bool result = aggregator.TryWriteSnapshot(tooShort, out int written);
            Assert.IsFalse(result);
            Assert.AreEqual(0, written);
        }

        [Test]
        public void GetSnapshot_ReturnsListMatchingTryWriteSnapshot()
        {
            // 観測完了条件: GetSnapshot の内容が TryWriteSnapshot と一致すること。
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 2);

            var l0s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var l0s1 = new FixedValueSource("lipsync", blendShapeCount, value: 0.5f);
            var l1s0 = new FixedValueSource("analog-blendshape", blendShapeCount, value: 0.25f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (0, 1, l0s1),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.6f);
            weightBuffer.SetWeight(0, 1, 0.5f);    // Σ = 1.1 → layer 0 Saturated
            weightBuffer.SetWeight(1, 0, 0.2f);    // layer 1 not saturated

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var listSnapshot = aggregator.GetSnapshot();
            Assert.IsInstanceOf<IReadOnlyList<LayerSourceWeightEntry>>(listSnapshot);
            Assert.AreEqual(3, listSnapshot.Count, "登録 source 分のエントリ数");

            Span<LayerSourceWeightEntry> spanSnapshot = new LayerSourceWeightEntry[4];
            Assert.IsTrue(aggregator.TryWriteSnapshot(spanSnapshot, out int written));
            Assert.AreEqual(3, written);

            for (int i = 0; i < written; i++)
            {
                Assert.AreEqual(spanSnapshot[i], listSnapshot[i],
                    $"TryWriteSnapshot と GetSnapshot の内容が一致すること (i={i})");
            }

            // layer 0 のエントリは Saturated=true, layer 1 のエントリは false
            Assert.IsTrue(listSnapshot[0].Saturated);
            Assert.IsTrue(listSnapshot[1].Saturated);
            Assert.IsFalse(listSnapshot[2].Saturated);
        }

        [Test]
        public void TryWriteSnapshot_SteadyStateCalls_DoNotAllocate()
        {
            // 観測完了条件: TryWriteSnapshot が pre-allocated バッファコピーで 0-alloc。
            const int blendShapeCount = 8;
            var profile = BuildProfile(layerCount: 2);

            var l0s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var l0s1 = new FixedValueSource("lipsync", blendShapeCount, value: 0.5f);
            var l1s0 = new FixedValueSource("analog-blendshape", blendShapeCount, value: 0.25f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (0, 1, l0s1),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);

            weightBuffer.SetWeight(0, 0, 0.5f);
            weightBuffer.SetWeight(0, 1, 0.5f);
            weightBuffer.SetWeight(1, 0, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            var snapshotBuffer = new LayerSourceWeightEntry[8];

            // ウォームアップ (JIT / 初回 Id キャッシュ構築等を排除)
            for (int i = 0; i < 10; i++)
            {
                aggregator.TryWriteSnapshot(snapshotBuffer, out _);
            }

            // 計測: 多数回呼出しで GC アロケーションが発生しないこと。
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(false);

            for (int i = 0; i < 1000; i++)
            {
                aggregator.TryWriteSnapshot(snapshotBuffer, out _);
            }

            long after = GC.GetTotalMemory(false);
            long allocated = after - before;

            // GC.GetTotalMemory は厳密ではないが、1000 回呼出しで顕著な増加があれば検出可能。
            Assert.LessOrEqual(allocated, 0,
                $"TryWriteSnapshot は 0-alloc であること (差分: {allocated} bytes)");
        }

        // ----- verbose logging の per-layer per-second レートリミッタ -----

        /// <summary>
        /// <see cref="LayerInputSourceAggregator.SetVerboseLogging(bool)"/> を true にしたうえで
        /// 1.0 秒のウィンドウ内に 100 回 <c>Aggregate</c> を回しても、同一レイヤーのログは
        /// 1 回だけ出力されること。
        /// 1.0 秒経過後にもう 1 回出力できること。
        /// </summary>
        [Test]
        public void Aggregate_VerboseLogging_RateLimitsTo_OncePerSecondPerLayer()
        {
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 1);

            var s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, s0) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.5f);

            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount, time);

            // 時刻 0.0 で verbose ON。次回ログ可能時刻は 0.0 にリセットされる。
            aggregator.SetVerboseLogging(true);

            // 1 回目: 時刻 0.0 でログ 1 回 (layer 0 について)。照合はクラスタグとレイヤー番号のみ。
            LogAssert.Expect(LogType.Log,
                new Regex(@"\[LayerInputSourceAggregator\] layer 0"));

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            // 1.0 秒以内に 100 回 Aggregate を回す。ログは最初の 1 回だけ。
            // (時刻は固定 0.0 → 0.99 の範囲で段階的に進め、1.0 を越えないようにする)
            for (int i = 0; i < 100; i++)
            {
                time.UnscaledTimeSeconds = 0.99 * i / 99.0; // 0.0 → 0.99
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }

            // 2 回目のログは 1.0 秒後に許可される。
            LogAssert.Expect(LogType.Log,
                new Regex(@"\[LayerInputSourceAggregator\] layer 0"));

            time.UnscaledTimeSeconds = 1.0;
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            // さらに直後 (1.0 秒未満) の連続 Aggregate では追加ログは出ない。
            for (int i = 0; i < 10; i++)
            {
                time.UnscaledTimeSeconds = 1.0 + 0.09 * i / 9.0; // 1.0 → 1.09
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }
        }

        /// <summary>
        /// 複数レイヤー構成で各レイヤーが独立にレート制限されること。
        /// 1 秒ウィンドウ内は layer 0 / layer 1 がそれぞれ 1 回ずつしかログを出さない。
        /// </summary>
        [Test]
        public void Aggregate_VerboseLogging_RateLimitsIndependentlyPerLayer()
        {
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 2);

            var l0s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var l1s0 = new FixedValueSource("lipsync", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.25f);
            weightBuffer.SetWeight(1, 0, 0.75f);

            var time = new ManualTimeProvider { UnscaledTimeSeconds = 5.0 };
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount, time);
            aggregator.SetVerboseLogging(true);

            LogAssert.Expect(LogType.Log,
                new Regex(@"\[LayerInputSourceAggregator\] layer 0"));
            LogAssert.Expect(LogType.Log,
                new Regex(@"\[LayerInputSourceAggregator\] layer 1"));

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[2];

            // 1 秒以内の多数回 Aggregate。各レイヤーのログはそれぞれ 1 回のみ。
            for (int i = 0; i < 50; i++)
            {
                time.UnscaledTimeSeconds = 5.0 + 0.99 * i / 49.0;
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }
        }

        /// <summary>
        /// verbose OFF の間は <c>Aggregate</c> を何度呼んでも診断ログは出ないこと。
        /// 再度 ON にすると、ON 時刻を起点にレート制限がリセットされ、次回 <c>Aggregate</c> でログが出せる。
        /// </summary>
        [Test]
        public void Aggregate_VerboseLogging_WhenDisabled_DoesNotLog()
        {
            const int blendShapeCount = 1;
            var profile = BuildProfile(layerCount: 1);
            var s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)> { (0, 0, s0) };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 1f);

            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount, time);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];

            // verbose OFF 状態での Aggregate はログ禁止。LogAssert.Expect を記述せず、想定外ログを検知する。
            for (int i = 0; i < 20; i++)
            {
                time.UnscaledTimeSeconds = 0.05 * i;
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }

            // ON 後、次の Aggregate でログが出る。
            time.UnscaledTimeSeconds = 10.0;
            aggregator.SetVerboseLogging(true);
            LogAssert.Expect(LogType.Log,
                new Regex(@"\[LayerInputSourceAggregator\] layer 0"));
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            // OFF に戻すと以降ログは出ない。
            aggregator.SetVerboseLogging(false);
            for (int i = 0; i < 20; i++)
            {
                time.UnscaledTimeSeconds = 10.0 + 10.0 * i; // 長時間経過させてもログは出ない
                aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            }
        }

        // ----- 診断ログ rate-limiter の長時間安定性 -----

        /// <summary>
        /// verbose ログを 10 分間 ON にした状態で、想定レート
        /// (per-layer per-second) 以上のログが出ないことを検証する補助テスト
        /// (長時間安定性カバレッジ)。
        /// </summary>
        /// <remarks>
        /// <para>
        /// <see cref="ManualTimeProvider"/> を 0〜600 秒まで 0.01 秒刻みで前進させ、
        /// <see cref="LayerInputSourceAggregator.Aggregate(float, System.Span{LayerBlender.LayerInput})"/>
        /// を 60,001 回呼び出したうえで、per-layer の verbose ログ件数がレートリミッタ契約
        /// (1 秒 1 回) から長時間でもドリフトしていないことを
        /// <see cref="UnityEngine.Application.logMessageReceived"/> でカウントして検証する。
        /// </para>
        /// <para>
        /// 想定ログ件数: 秒境界 t = 0, 1, 2, ..., 600 の 601 回分が各レイヤーで出力される。
        /// それ以上出ていれば「窓が崩れてドリフトしている」、それ未満であれば
        /// 「ログが抜けている / 時刻比較がずれている」と判定し Fail させる。
        /// </para>
        /// <para>
        /// 時刻は <c>i / stepsPerSecond</c> (double) で算出し、秒境界 i = 0, 100, 200, ...
        /// でちょうど整数秒になるように整数演算ベースで前進させる。
        /// </para>
        /// </remarks>
        [Test]
        public void Aggregate_VerboseLogging_10MinuteStability_RespectsRateLimitWithoutDrift()
        {
            const int blendShapeCount = 1;
            const int layerCount = 2;
            const int stepsPerSecond = 100;
            const int totalSeconds = 600; // 10 分
            const int expectedLogsPerLayer = totalSeconds + 1; // t=0,1,...,600 の 601 件
            long totalSteps = (long)stepsPerSecond * totalSeconds;

            var profile = BuildProfile(layerCount);
            var l0s0 = new FixedValueSource("osc", blendShapeCount, value: 1f);
            var l1s0 = new FixedValueSource("lipsync", blendShapeCount, value: 1f);
            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, l0s0),
                (1, 0, l1s0),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.5f);
            weightBuffer.SetWeight(1, 0, 0.5f);

            var time = new ManualTimeProvider { UnscaledTimeSeconds = 0.0 };
            var aggregator = new LayerInputSourceAggregator(
                registry, weightBuffer, blendShapeCount, time);

            var layerLogCounts = new int[layerCount];
            var logPattern = new Regex(@"\[LayerInputSourceAggregator\] layer (\d+): weights=");

            UnityEngine.Application.LogCallback handler = (condition, stackTrace, type) =>
            {
                if (type != LogType.Log)
                {
                    return;
                }
                var match = logPattern.Match(condition);
                if (!match.Success)
                {
                    return;
                }
                int idx = int.Parse(match.Groups[1].Value);
                if ((uint)idx < (uint)layerLogCounts.Length)
                {
                    layerLogCounts[idx]++;
                }
            };

            UnityEngine.Application.logMessageReceived += handler;
            try
            {
                aggregator.SetVerboseLogging(true);
                var outputPerLayer = new LayerBlender.LayerInput[layerCount];

                float deltaTime = 1f / stepsPerSecond;
                for (long i = 0; i <= totalSteps; i++)
                {
                    time.UnscaledTimeSeconds = (double)i / stepsPerSecond;
                    aggregator.Aggregate(deltaTime, outputPerLayer);
                }
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= handler;
            }

            for (int l = 0; l < layerCount; l++)
            {
                Assert.AreEqual(expectedLogsPerLayer, layerLogCounts[l],
                    $"layer {l}: 10 分間 (60,001 回 Aggregate) の verbose ログ件数が " +
                    $"想定レートからドリフトした (想定 {expectedLogsPerLayer} 件、実測 {layerLogCounts[l]} 件)");
            }
        }

        // ----- layer ContributeMask の OR 集約 -----

        /// <summary>
        /// 指定 index のみ contribute する <see cref="IInputSource"/> フェイク。値は常に 1.0 を書込む。
        /// </summary>
        private sealed class MaskedValueSource : IInputSource
        {
            private readonly bool _isValid;

            public MaskedValueSource(string id, int blendShapeCount, bool isValid, params int[] contributeIndexes)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = new BitArray(blendShapeCount, false);
                for (int i = 0; i < contributeIndexes.Length; i++)
                {
                    int index = contributeIndexes[i];
                    if ((uint)index < (uint)blendShapeCount)
                    {
                        ContributeMask[index] = true;
                    }
                }
                _isValid = isValid;
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                if (!_isValid)
                {
                    return false;
                }

                int len = Math.Min(output.Length, BlendShapeCount);
                for (int i = 0; i < len; i++)
                {
                    output[i] = 1f;
                }
                return true;
            }
        }

        private static LayerBlender.LayerInput[] AggregateSingleLayer(
            int blendShapeCount,
            IReadOnlyList<(int layerIdx, int sourceIdx, IInputSource source)> bindings)
        {
            var profile = BuildProfile(layerCount: 1);
            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(
                registry.LayerCount,
                Math.Max(1, registry.MaxSourcesPerLayer));

            for (int sourceIdx = 0; sourceIdx < registry.GetSourceCountForLayer(0); sourceIdx++)
            {
                weightBuffer.SetWeight(0, sourceIdx, 1f);
            }

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            var outputPerLayer = new LayerBlender.LayerInput[registry.LayerCount];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);
            return outputPerLayer;
        }

        [Test]
        public void Aggregate_MultipleSourcesContributeSameIndex_OrsMaskIndexToTrue()
        {
            const int blendShapeCount = 4;
            var source0 = new MaskedValueSource("source0", blendShapeCount, true, 1);
            var source1 = new MaskedValueSource("source1", blendShapeCount, true, 1, 2);

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>
                {
                    (0, 0, source0),
                    (0, 1, source1),
                });

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null,
                "Aggregator は layer ごとの OR 集約済み ContributeMask を LayerInput に渡す必要がある。");
            Assert.That(mask[1], Is.True,
                "複数 source が同じ index に contribute する場合、layer mask は OR で true になること。");
        }

        [Test]
        public void Aggregate_AllSourcesDoNotContributeIndex_LeavesMaskIndexFalse()
        {
            const int blendShapeCount = 4;
            var source0 = new MaskedValueSource("source0", blendShapeCount, true, 0);
            var source1 = new MaskedValueSource("source1", blendShapeCount, true, 2);

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>
                {
                    (0, 0, source0),
                    (0, 1, source1),
                });

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null,
                "Aggregator は全 false を表現できる layer mask 参照を渡す必要がある。");
            Assert.That(mask[1], Is.False,
                "どの valid source も contribute しない index は false のまま残ること。");
            Assert.That(mask[3], Is.False,
                "全 source が非 contribute の index は OR 集約後も false のままであること。");
        }

        [Test]
        public void Aggregate_InvalidSourceHasMask_ExcludesMaskFromOrAggregation()
        {
            const int blendShapeCount = 3;
            var valid = new MaskedValueSource("valid", blendShapeCount, true, 0);
            var invalid = new MaskedValueSource("invalid", blendShapeCount, false, 1);

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>
                {
                    (0, 0, valid),
                    (0, 1, invalid),
                });

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null);
            Assert.That(mask[0], Is.True,
                "valid source の contribute index は layer mask に含まれること。");
            Assert.That(mask[1], Is.False,
                "TryWriteValues が false の source の mask は OR 集約対象外であること。");
        }

        [Test]
        public void Aggregate_NoSourcesRegistered_CreatesAllFalseLayerMask()
        {
            const int blendShapeCount = 3;

            LogAssert.Expect(LogType.Warning,
                new Regex("LayerInputSourceAggregator.*layer 0"));

            var outputPerLayer = AggregateSingleLayer(
                blendShapeCount,
                new List<(int, int, IInputSource)>());

            BitArray mask = outputPerLayer[0].ContributeMask;
            Assert.That(mask, Is.Not.Null,
                "source 0 本の layer でも全 false の layer mask を渡す必要がある。");
            Assert.That(mask.Length, Is.EqualTo(blendShapeCount));
            for (int i = 0; i < blendShapeCount; i++)
            {
                Assert.That(mask[i], Is.False,
                    $"source 0 本の layer mask は全 false であること (index={i})。");
            }
        }

        // ----- ILayerSourceValueObserver への pre-weight 値通知 -----

        /// <summary>観測された source 値をそのまま記録する <see cref="ILayerSourceValueObserver"/>。</summary>
        private sealed class RecordingObserver : ILayerSourceValueObserver
        {
            public readonly List<Call> Calls = new List<Call>();

            public void OnSourceValuesObserved(
                int layerIdx,
                int sourceIdx,
                InputSourceId sourceId,
                bool isValid,
                ReadOnlySpan<float> preWeightValues)
            {
                var copy = new float[preWeightValues.Length];
                preWeightValues.CopyTo(copy);
                Calls.Add(new Call(layerIdx, sourceIdx, sourceId.Value, isValid, copy));
            }

            public readonly struct Call
            {
                public Call(int layerIdx, int sourceIdx, string sourceId, bool isValid, float[] values)
                {
                    LayerIdx = layerIdx;
                    SourceIdx = sourceIdx;
                    SourceId = sourceId;
                    IsValid = isValid;
                    Values = values;
                }

                public int LayerIdx { get; }
                public int SourceIdx { get; }
                public string SourceId { get; }
                public bool IsValid { get; }
                public float[] Values { get; }
            }
        }

        /// <summary>
        /// index ごとに異なる固定値を書込む <see cref="IInputSource"/> フェイク。
        /// BlendShapeCount は渡した値配列の長さで決まる。
        /// </summary>
        private sealed class PerIndexValueSource : IInputSource
        {
            private readonly float[] _values;
            private readonly bool _isValid;

            public PerIndexValueSource(string id, bool isValid, params float[] values)
            {
                Id = id;
                _isValid = isValid;
                _values = values ?? Array.Empty<float>();
                BlendShapeCount = _values.Length;
                ContributeMask = new BitArray(BlendShapeCount, true);
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime) { }

            public bool TryWriteValues(Span<float> output)
            {
                if (!_isValid)
                {
                    return false;
                }

                _values.AsSpan().CopyTo(output);
                return true;
            }
        }

        [Test]
        public void Aggregate_WithObserver_ReportsPreWeightValuesPerSource()
        {
            const int blendShapeCount = 3;
            var profile = BuildProfile(layerCount: 1);
            var source0 = new PerIndexValueSource("osc", isValid: true, 0.2f, 0.4f, 0.6f);
            var source1 = new PerIndexValueSource("lipsync", isValid: true, 0.9f, 0.1f, 0.3f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, source0),
                (0, 1, source1),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.25f);
            weightBuffer.SetWeight(0, 1, 0.75f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            var observer = new RecordingObserver();
            aggregator.SetSourceValueObserver(observer);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Assert.That(observer.Calls.Count, Is.EqualTo(2));
            Assert.That(observer.Calls[0].LayerIdx, Is.EqualTo(0));
            Assert.That(observer.Calls[0].SourceIdx, Is.EqualTo(0));
            Assert.That(observer.Calls[0].SourceId, Is.EqualTo("osc"));
            Assert.That(observer.Calls[0].IsValid, Is.True);
            Assert.That(observer.Calls[0].Values, Is.EqualTo(new[] { 0.2f, 0.4f, 0.6f }));

            Assert.That(observer.Calls[1].LayerIdx, Is.EqualTo(0));
            Assert.That(observer.Calls[1].SourceIdx, Is.EqualTo(1));
            Assert.That(observer.Calls[1].SourceId, Is.EqualTo("lipsync"));
            Assert.That(observer.Calls[1].IsValid, Is.True);
            Assert.That(observer.Calls[1].Values, Is.EqualTo(new[] { 0.9f, 0.1f, 0.3f }));
        }

        [Test]
        public void Aggregate_InvalidSource_ObserverSeesFalseAndZeroedScratch()
        {
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);
            var invalid = new PerIndexValueSource("invalid", isValid: false, 1f, 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, invalid),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 1f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);
            var observer = new RecordingObserver();
            aggregator.SetSourceValueObserver(observer);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Assert.That(observer.Calls.Count, Is.EqualTo(1));
            Assert.That(observer.Calls[0].SourceId, Is.EqualTo("invalid"));
            Assert.That(observer.Calls[0].IsValid, Is.False);
            Assert.That(observer.Calls[0].Values, Is.EqualTo(new[] { 0f, 0f }));
        }

        [Test]
        public void Aggregate_WithoutObserver_StillAggregatesNormally()
        {
            const int blendShapeCount = 2;
            var profile = BuildProfile(layerCount: 1);
            var source = new PerIndexValueSource("osc", isValid: true, 0.5f, 1f);

            var bindings = new List<(int, int, IInputSource)>
            {
                (0, 0, source),
            };

            using var registry = new LayerInputSourceRegistry(profile, blendShapeCount, bindings);
            using var weightBuffer = new LayerInputSourceWeightBuffer(registry.LayerCount, registry.MaxSourcesPerLayer);
            weightBuffer.SetWeight(0, 0, 0.5f);

            var aggregator = new LayerInputSourceAggregator(registry, weightBuffer, blendShapeCount);

            Span<LayerBlender.LayerInput> outputPerLayer = new LayerBlender.LayerInput[1];
            aggregator.Aggregate(deltaTime: 0f, outputPerLayer);

            Assert.That(outputPerLayer[0].BlendShapeValues.Span.ToArray(), Is.EqualTo(new[] { 0.25f, 0.5f }));
        }
    }
}
