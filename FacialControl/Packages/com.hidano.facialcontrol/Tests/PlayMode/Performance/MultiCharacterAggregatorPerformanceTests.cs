using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using NUnit.Framework;
using UnityEngine;
using Hidano.FacialControl.Domain.Interfaces;
using Hidano.FacialControl.Domain.Models;
using Hidano.FacialControl.Domain.Services;

using Hidano.FacialControl.Testing;
namespace Hidano.FacialControl.Tests.PlayMode.Performance
{
    /// <summary>
    //: 10 体 × 3 layer × 4 source × 200 BlendShape のフレーム時間が
    /// 60 FPS 相当 (16.6ms) のフレームバジェット内に収まることを検証する
    /// 。
    /// </summary>
    /// <remarks>
    /// 観測完了条件: 平均フレーム時間が 16.6ms 以内、スパイクが許容範囲に収まること。
    /// 1 体あたり 1 組の (Registry, WeightBuffer, Aggregator) が
    /// <see cref="Adapters.Playable.FacialController"/> 1 インスタンスに対応する
    /// (8.2 で組み立てられるパイプラインと同じ構成)。本テストは
    /// SkinnedMeshRenderer / PlayableGraph などの周辺コストを除き、
    /// 同時稼働時の Aggregator パイプライン自体のスループットを計測する。
    /// </remarks>
    [TestFixture]
    [MediumTest]
    public class MultiCharacterAggregatorPerformanceTests : SizedTestFixture
    {
        private const int CharacterCount = 10;
        private const int LayerCount = 3;
        private const int SourcesPerLayer = 4;
        private const int BlendShapeCount = 200;
        private const int FramesToMeasure = 60;
        private const double FrameBudgetMs = 16.6;
        // 1 フレームのスパイク許容上限。バックグラウンド GC や OS スケジューリング揺らぎを
        // 吸収するため、フレーム予算の 2 倍 (= 30fps 相当) までを許容する。
        private const double SpikeBudgetMs = FrameBudgetMs * 2.0;

        /// <summary>
        /// 加重和ホットパスを通すための固定値を書込む値提供型フェイク。
        /// GC ゼロかつ計算負荷が ≒ <see cref="BlendShapeCount"/> ループ 1 回分なので、
        /// Aggregator の実コードパスを正しく走らせつつ計測ノイズを最小化できる。
        /// </summary>
        private sealed class FixedValueSource : IInputSource
        {
            private readonly float _value;

            public FixedValueSource(string id, int blendShapeCount, float value)
            {
                Id = id;
                BlendShapeCount = blendShapeCount;
                ContributeMask = new BitArray(blendShapeCount, true);
                _value = value;
            }

            public string Id { get; }
            public InputSourceType Type => InputSourceType.ValueProvider;
            public int BlendShapeCount { get; }
            public BitArray ContributeMask { get; }

            public void Tick(float deltaTime)
            {
            }

            public bool TryWriteValues(Span<float> output)
            {
                int len = output.Length < BlendShapeCount ? output.Length : BlendShapeCount;
                for (int i = 0; i < len; i++)
                {
                    output[i] = _value;
                }
                return true;
            }
        }

        /// <summary>
        /// 1 体分の Aggregator パイプライン一式。Dispose で NativeArray を解放する。
        /// </summary>
        private sealed class CharacterPipeline : IDisposable
        {
            public LayerInputSourceRegistry Registry { get; }
            public LayerInputSourceWeightBuffer WeightBuffer { get; }
            public LayerInputSourceAggregator Aggregator { get; }
            public int[] Priorities { get; }
            public float[] LayerWeights { get; }
            public LayerBlender.LayerInput[] OutputPerLayer { get; }

            public CharacterPipeline(FacialProfile profile)
            {
                var bindings = new List<(int, int, IInputSource)>(LayerCount * SourcesPerLayer);
                for (int l = 0; l < LayerCount; l++)
                {
                    for (int s = 0; s < SourcesPerLayer; s++)
                    {
                        // ソースごとに少し異なる値を返し、加重和の結果がレイヤー間で
                        // 同一にならないようにする (CPU 側の分岐予測ノイズも均す)。
                        float value = ((l * SourcesPerLayer + s) % 7) / 6f;
                        bindings.Add((l, s, new FixedValueSource($"src_{l}_{s}", BlendShapeCount, value)));
                    }
                }

                Registry = new LayerInputSourceRegistry(profile, BlendShapeCount, bindings);
                WeightBuffer = new LayerInputSourceWeightBuffer(
                    Registry.LayerCount, Registry.MaxSourcesPerLayer);

                // 全 (layer, source) に既定 weight=0.25 を入れて 4 source の加重和を成立させる。
                for (int l = 0; l < LayerCount; l++)
                {
                    for (int s = 0; s < SourcesPerLayer; s++)
                    {
                        WeightBuffer.SetWeight(l, s, 0.25f);
                    }
                }

                Aggregator = new LayerInputSourceAggregator(Registry, WeightBuffer, BlendShapeCount);

                Priorities = new int[LayerCount];
                LayerWeights = new float[LayerCount];
                OutputPerLayer = new LayerBlender.LayerInput[LayerCount];
                for (int l = 0; l < LayerCount; l++)
                {
                    Priorities[l] = l;
                    LayerWeights[l] = 1f;
                }
            }

            public void Dispose()
            {
                Registry?.Dispose();
                WeightBuffer?.Dispose();
            }
        }

        private static FacialProfile BuildProfile()
        {
            var layers = new LayerDefinition[LayerCount];
            for (int i = 0; i < LayerCount; i++)
            {
                layers[i] = new LayerDefinition($"layer{i}", priority: i, ExclusionMode.LastWins);
            }
            return new FacialProfile("1.0", layers);
        }

        [Test]
        public void TenCharacters_3Layer_4Source_200BlendShape_MaintainsFrameBudget()
        {
            var profile = BuildProfile();
            var pipelines = new CharacterPipeline[CharacterCount];
            try
            {
                for (int c = 0; c < CharacterCount; c++)
                {
                    pipelines[c] = new CharacterPipeline(profile);
                }

                // ウォームアップ: JIT / 初回 SwapIfDirty / pending dict プールの初期化を済ませる。
                // 計測本体と同じコードパスを通すことが目的なので 3 frame 分回す。
                for (int frame = 0; frame < 3; frame++)
                {
                    for (int c = 0; c < pipelines.Length; c++)
                    {
                        var p = pipelines[c];
                        for (int l = 0; l < LayerCount; l++)
                        {
                            for (int s = 0; s < SourcesPerLayer; s++)
                            {
                                p.WeightBuffer.SetWeight(l, s, 0.25f);
                            }
                        }
                        p.Aggregator.Aggregate(
                            deltaTime: 0.016f,
                            new ReadOnlySpan<int>(p.Priorities),
                            new ReadOnlySpan<float>(p.LayerWeights),
                            new Span<LayerBlender.LayerInput>(p.OutputPerLayer));
                    }
                }

                // 計測前に managed ヒープを安定させる (GC ノイズ最小化)。
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                double totalMs = 0.0;
                double maxFrameMs = 0.0;
                var sw = new Stopwatch();

                for (int frame = 0; frame < FramesToMeasure; frame++)
                {
                    sw.Restart();

                    for (int c = 0; c < pipelines.Length; c++)
                    {
                        var p = pipelines[c];

                        // 毎フレーム異なる値を書込んで SwapIfDirty の copy-forward 経路を必ず通す。
                        // 1 体あたり 12 set / フレーム → 10 体で 120 set / フレーム。
                        for (int l = 0; l < LayerCount; l++)
                        {
                            for (int s = 0; s < SourcesPerLayer; s++)
                            {
                                float value = (((frame + l + s) % 5) + 1) * 0.1f; // 0.1〜0.5
                                p.WeightBuffer.SetWeight(l, s, value);
                            }
                        }

                        p.Aggregator.Aggregate(
                            deltaTime: 0.016f,
                            new ReadOnlySpan<int>(p.Priorities),
                            new ReadOnlySpan<float>(p.LayerWeights),
                            new Span<LayerBlender.LayerInput>(p.OutputPerLayer));
                    }

                    sw.Stop();
                    double frameMs = sw.Elapsed.TotalMilliseconds;
                    totalMs += frameMs;
                    if (frameMs > maxFrameMs)
                    {
                        maxFrameMs = frameMs;
                    }
                }

                double avgMs = totalMs / FramesToMeasure;

                UnityEngine.Debug.Log(
                    $"[10.5 perf] 10 体 × {LayerCount} layer × {SourcesPerLayer} source × {BlendShapeCount} BS: " +
                    $"avg={avgMs:F3}ms, max={maxFrameMs:F3}ms, budget={FrameBudgetMs}ms (spike<= {SpikeBudgetMs}ms)");

                // 平均フレーム時間が 60 FPS バジェット (16.6ms) 以内に収まること。
                Assert.Less(avgMs, FrameBudgetMs,
                    $"10 体同時稼働の平均フレーム時間が {avgMs:F3}ms で 60 FPS バジェット {FrameBudgetMs}ms を超えました " +
                    $"(max={maxFrameMs:F3}ms)");

                // スパイクが許容範囲 (フレーム予算の 2 倍) に収まること。
                Assert.Less(maxFrameMs, SpikeBudgetMs,
                    $"10 体同時稼働の最大フレーム時間が {maxFrameMs:F3}ms で許容範囲 {SpikeBudgetMs}ms を超えました " +
                    $"(avg={avgMs:F3}ms)");
            }
            finally
            {
                for (int c = 0; c < pipelines.Length; c++)
                {
                    pipelines[c]?.Dispose();
                }
            }
        }
    }
}
